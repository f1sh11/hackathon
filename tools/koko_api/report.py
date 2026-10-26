"""Static, credential-redacted evidence reports for the Koko HTTP test harness."""

from __future__ import annotations

import csv
import html
import io
import json
import os
from pathlib import Path
import re
import tempfile
from typing import Any


_SECRET_KEYS = {
    "authorization", "proxyauthorization", "cookie", "setcookie", "apikey",
    "xapikey", "nvidiaapikey", "openaiapikey", "googlemapsapikey", "accesscode",
    "xsydneybuddyaccesscode", "token", "accesstoken", "refreshtoken", "idtoken",
    "password", "passwd", "pwd", "secret", "clientsecret", "clientsecretvalue",
    "credentials", "connectionstring", "connectionstringsydneybuddy",
}
_BEARER = re.compile(r"\bBearer\s+[A-Za-z0-9._~+/=\-]+", re.IGNORECASE)
_NVIDIA_KEY = re.compile(r"\bnvapi-[A-Za-z0-9_\-]+")
_URL_CREDENTIALS = re.compile(r"(https?://)[^/\s@]+@", re.IGNORECASE)
_INLINE_SECRET = re.compile(
    r"\b(password|pwd|token|api[-_]?key|access[-_]?token|access[-_]?code|client[-_]?secret)\s*([=:])\s*(\"[^\"]*\"|'[^']*'|[^\s;&,\"']+)",
    re.IGNORECASE,
)


def redact(value: Any) -> Any:
    """Remove credential-shaped data without mutating the recorded result.

    The transport also redacts exact configured secret values. This second layer
    protects report consumers when they call the renderer independently.
    """
    if isinstance(value, dict):
        result = {}
        for key, item in value.items():
            normalized = re.sub(r"[^a-z0-9]", "", str(key).lower())
            result[str(key)] = "[REDACTED]" if normalized in _SECRET_KEYS else redact(item)
        return result
    if isinstance(value, (tuple, list)):
        return [redact(item) for item in value]
    if isinstance(value, str):
        value = _BEARER.sub("Bearer [REDACTED]", value)
        value = _NVIDIA_KEY.sub("[REDACTED]", value)
        value = _URL_CREDENTIALS.sub(r"\1[REDACTED]@", value)
        return _INLINE_SECRET.sub(lambda match: match[1] + match[2] + "[REDACTED]", value)
    return value


def _json(value: Any) -> str:
    return json.dumps(value, ensure_ascii=False, sort_keys=True, default=str)


def _pretty(value: Any) -> str:
    return json.dumps(value, ensure_ascii=False, indent=2, sort_keys=True, default=str)


def _text(value: Any) -> str:
    if value is None:
        return "未提供"
    if isinstance(value, (dict, list, tuple, bool)):
        return _json(value)
    return str(value)


def _e(value: Any) -> str:
    return html.escape(_text(value), quote=True)


def _cell(value: Any) -> str:
    value = _text(value)
    # Excel and similar tools may execute formula cells even inside quoted CSV.
    if value.lstrip(" \t\r\n").startswith(("=", "+", "-", "@")):
        return "'" + value
    return value


def _mapping(value: Any) -> dict:
    return value if isinstance(value, dict) else {}


def _items(value: Any) -> list:
    return value if isinstance(value, list) else []


def _session(step: dict) -> dict:
    after = _mapping(step.get("after"))
    response = _mapping(step.get("response"))
    value = after.get("session") or response.get("session")
    if isinstance(value, dict):
        return value
    for event in reversed(_items(response.get("events"))):
        if isinstance(event, dict) and isinstance(event.get("session"), dict):
            return event["session"]
    return {}


def _last_assistant(session: dict) -> dict:
    for message in reversed(_items(session.get("messages"))):
        if isinstance(message, dict) and message.get("role") == "assistant":
            return message
    return {}


def _reply(step: dict, session: dict) -> Any:
    response = _mapping(step.get("response"))
    for event in reversed(_items(response.get("events"))):
        if isinstance(event, dict) and event.get("type") == "reply" and event.get("text"):
            return event["text"]
    return _last_assistant(session).get("text")


def _badge(status: Any) -> str:
    status = str(status or "NOT_RUN")
    css = {"PASS": "pass", "FAIL": "fail", "NOT_RUN": "not-run"}.get(status, "not-run")
    return f'<span class="badge {css}">{_e(status)}</span>'


def _case_model_calls(case: dict) -> Any:
    trace = _mapping(case.get("modelTrace"))
    if isinstance(trace.get("totalCalls"), int):
        return trace["totalCalls"]
    if isinstance(trace.get("calls"), list):
        return len(trace["calls"])
    return None


def _model_calls(result: dict) -> Any:
    environment = _mapping(result.get("environment"))
    if "modelCallsObserved" in environment:
        return environment["modelCallsObserved"]
    cases = [_mapping(case) for case in _items(result.get("cases"))]
    counts = [_case_model_calls(case) for case in cases]
    if cases and all(count is not None or case.get("status") == "NOT_RUN" for case, count in zip(cases, counts)):
        return sum(count for count in counts if count is not None)
    return None


def _block(title: str, value: Any, *, expanded: bool = False) -> str:
    if value is None:
        return f'<section class="datum"><h4>{_e(title)}</h4><p class="muted">未提供／未读取</p></section>'
    flag = " open" if expanded else ""
    return f'<details{flag}><summary>{_e(title)}</summary><pre>{html.escape(_pretty(value), quote=True)}</pre></details>'


def _assertions(items: Any) -> str:
    assertions = _items(items)
    if not assertions:
        return '<p class="muted">本轮未定义自动断言。</p>'
    rows = []
    for assertion in assertions:
        assertion = _mapping(assertion)
        outcome = assertion.get("passed")
        status = "PASS" if outcome is True else "FAIL" if outcome is False else "NOT_RUN"
        rows.append(
            "<tr><td>" + _badge(status) + "</td><td>" + _e(assertion.get("path", ""))
            + "</td><td>" + _e(assertion.get("message", ""))
            + "</td><td><code>" + _e(assertion.get("expected"))
            + "</code></td><td><code>" + _e(assertion.get("actual")) + "</code></td></tr>"
        )
    return '<div class="table-wrap"><table><thead><tr><th>结果</th><th>路径</th><th>说明</th><th>预期</th><th>实际</th></tr></thead><tbody>' + "".join(rows) + "</tbody></table></div>"


def _step_html(step: dict, index: int) -> str:
    session = _session(step)
    before, after = _mapping(step.get("before")), _mapping(step.get("after"))
    response = _mapping(step.get("response"))
    ready = after.get("derivedReady")
    ready_text = "是" if ready is True else "否" if ready is False else "未知"
    question = {
        key: session.get(key) for key in ("nextField", "question", "options", "completed", "total")
    }
    question["quickReplies"] = _last_assistant(session).get("quickReplies")
    prefs = {
        key: session.get(key) for key in ("pendingPreferences", "tripPreferences", "travelDna", "pendingMemory")
    }
    parts = [
        f'<article class="step"><h3>第 {_e(step.get("index", index))} 轮 · {_e(step.get("op", ""))} {_badge(step.get("status"))}</h3>',
        f'<p class="muted">HTTP {_e(response.get("httpStatus"))} · {_e(step.get("durationMs"))} ms</p>',
    ]
    if step.get("reason"):
        parts.append(f'<p class="reason">{_e(step["reason"])}</p>')
    if response.get("error"):
        parts.append(f'<p class="reason">接口／流错误：{_e(response["error"])}</p>')
    parts.extend([
        _block("用户输入 / 操作参数", step.get("input"), expanded=True),
        f'<section class="speech"><h4>当前 Koko 回复（或会话最后一条 assistant 消息）</h4><p>{_e(_reply(step, session))}</p></section>',
        _block("本轮字段变化 fieldChanges", step.get("fieldChanges"), expanded=True),
        _block("原始字段 fields", session.get("fields")),
        _block("本次旅行上下文 tripContext", session.get("tripContext")),
        _block("有效规划字段 planningFields", session.get("planningFields")),
        _block("下一步问题与选项（后端原字段）", question, expanded=True),
        _block("缺失重要需求 missingImportantFields", session.get("missingImportantFields"), expanded=True),
        f'<p><strong>后端完整性（工具推导）：{ready_text}</strong>；不是接口返回的 canGenerate，也不代表手机按钮已出现或可点击。</p>',
        _block("待确认 / 本次 / 已确认偏好", prefs, expanded=True),
        f'<p>记忆数量变化（相对本案例基线）：{_e(after.get("memoryDelta"))}</p>',
        _block("实际记忆：本轮之前 /memories", before.get("memories")),
        _block("实际记忆：本轮之后 /memories", after.get("memories")),
        "<h4>自动断言</h4>", _assertions(step.get("assertions")),
        _block("请求（已脱敏）", step.get("request")),
        _block("响应与 NDJSON 事件（已脱敏）", step.get("response")),
        _block("完整本轮证据", step), "</article>",
    ])
    return "".join(parts)


def _html(result: dict) -> str:
    mode = result.get("mode", "unknown")
    environment = _mapping(result.get("environment"))
    if mode == "fixture":
        mode_text = "固定桩模型（fixture）：只证明可重复接口逻辑，不代表真实模型表现。"
    elif mode == "live" and environment.get("modelConfigured") is False:
        mode_text = "live 环境但真实模型未配置；仅执行不依赖模型的接口检查，其余未运行，未使用桩回复。"
    elif mode == "live" and environment.get("modelConfigured") is True:
        mode_text = "live 环境（真实模型通道）：配置已提供；每个案例是否实际调用模型，以 modelTrace 为准。"
    elif mode == "live":
        mode_text = "live 环境：模型配置状态未提供；不能据此宣称真实模型已运行。"
    else:
        mode_text = "模型模式未提供：不能据此宣称真实模型已运行。"
    cases = _items(result.get("cases"))
    totals = {name: sum(_mapping(case).get("status") == name for case in cases) for name in ("PASS", "FAIL", "NOT_RUN")}
    summary_rows, sections = [], []
    for index, case in enumerate(cases, 1):
        case = _mapping(case)
        summary_rows.append(
            f'<tr><td><a href="#case-{index}">{_e(case.get("id", index))}</a></td>'
            f'<td>{_e(case.get("title", ""))}</td><td>{_badge(case.get("status"))}</td><td>{_e(_case_model_calls(case))}</td><td>{_e(case.get("reason", ""))}</td></tr>'
        )
        review = _items(case.get("humanReview"))
        manual = '<ul>' + ''.join(f'<li>{_e(item)}</li>' for item in review) + '</ul>' if review else '<p>未提供人工评阅提示。</p>'
        sections.append(
            f'<section class="case" id="case-{index}"><h2>{_e(case.get("id", index))} · {_e(case.get("title", ""))} {_badge(case.get("status"))}</h2>'
            f'<p>分类：{_e(case.get("category", ""))}</p><p class="reason">{_e(case.get("reason", ""))}</p>'
            '<aside><strong>人工评阅（未由自动断言判定）</strong>' + manual + '</aside>'
            + _block("模型调用证据 modelTrace", case.get("modelTrace"))
            + _assertions(case.get("assertions"))
            + ''.join(_step_html(_mapping(step), number) for number, step in enumerate(_items(case.get("steps")), 1))
            + '</section>'
        )
    return """<!doctype html>
<html lang="zh-CN"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Koko 对话与记忆接口测试报告</title>
<style>
:root{color-scheme:light;font-family:system-ui,sans-serif;color:#22333a;background:#f5f8f8}body{max-width:1180px;margin:0 auto;padding:28px 20px}h1{font-size:30px}h2{font-size:23px}h3{font-size:19px}h4{margin:14px 0 8px}a{color:#176b66}p,li{line-height:1.6}pre{white-space:pre-wrap;overflow-wrap:anywhere;font-size:12px;line-height:1.55;background:#f2f5f6;padding:12px;border-radius:7px}code{white-space:pre-wrap;overflow-wrap:anywhere}summary{cursor:pointer;font-weight:600;padding:9px 0}details{border-bottom:1px solid #dce6e7;padding:4px 0}.case{margin-top:28px;padding:24px;background:white;border:1px solid #d7e3e4;border-radius:12px}.step{margin:22px 0 0;padding:18px;border:1px solid #dce6e7;border-radius:8px}.badge{display:inline-block;border-radius:5px;padding:3px 8px;font-size:12px;font-weight:700}.pass{background:#dcf2e5;color:#155632}.fail{background:#fde4e4;color:#8f2424}.not-run{background:#e9edf1;color:#485665}.mode,aside{padding:14px;background:#fff3d5;border-left:4px solid #b37c13}.mode{margin:16px 0}.muted{color:#607279;font-size:13px}.reason{color:#8a3535;white-space:pre-wrap;overflow-wrap:anywhere}.speech{padding:12px 16px;background:#f8f1f2;border-left:4px solid #bd5366}.speech p{white-space:pre-wrap}.table-wrap{overflow-x:auto}table{border-collapse:collapse;width:100%;font-size:13px}th,td{text-align:left;vertical-align:top;border:1px solid #dce6e7;padding:9px;overflow-wrap:anywhere}th{background:#eaf2f2}.totals{display:flex;gap:15px;flex-wrap:wrap}footer{margin-top:26px;color:#607279;font-size:13px}
</style></head><body>
""" + (
        '<h1>Koko 对话与记忆接口测试报告</h1>'
        f'<p>运行 {_e(result.get("runId"))} · {_e(result.get("startedAt"))} → {_e(result.get("finishedAt"))}</p>'
        f'<div class="mode"><strong>{_e(mode_text)}</strong></div>'
        f'<p><strong>模型已配置：{_e(environment.get("modelConfigured"))}；已观测模型调用数：{_e(_model_calls(result))}</strong>。接口检查 PASS 不等于真实模型已调用。</p>'
        '<p>亲切程度和追问自然度需要人工评阅。接口结果不证明 iOS 的键盘、按钮、动画或跳转通过。</p>'
        '<div class="totals">' + ''.join(f'<p>{_badge(name)} {count}</p>' for name, count in totals.items()) + '</div>'
        + _block("版本、模型、提示哈希、测试时钟（Australia/Sydney）", result.get("environment"), expanded=True)
        + _block("运行备注", result.get("notes"))
        + '<h2>案例汇总</h2><div class="table-wrap"><table><thead><tr><th>案例</th><th>说明</th><th>状态</th><th>观测模型调用数</th><th>原因</th></tr></thead><tbody>'
        + ''.join(summary_rows) + '</tbody></table></div>' + ''.join(sections)
        + '<footer>证据来源：实际 HTTP 请求、NDJSON 事件及会话／记忆读取。原始脱敏证据见 run.json；逐轮数据见 turns.csv。缺失字段保留为未知，不自动补成成功。</footer></body></html>'
    )


def _csv_rows(headers: list[str], rows: list[dict]) -> str:
    stream = io.StringIO(newline="")
    writer = csv.DictWriter(stream, fieldnames=headers, lineterminator="\n")
    writer.writeheader()
    for row in rows:
        writer.writerow({key: _cell(row.get(key)) for key in headers})
    return stream.getvalue()


def _summary_csv(result: dict) -> str:
    environment = _mapping(result.get("environment"))
    headers = ["runId", "mode", "sourceCommit", "model", "modelConfigured", "modelCallsObserved", "caseModelCallsObserved", "caseId", "title", "category", "status", "reason", "stepCount", "passedAssertions", "failedAssertions", "humanReview", "sydneyClock"]
    rows = []
    for case in _items(result.get("cases")):
        case = _mapping(case)
        assertions = _items(case.get("assertions")) + [assertion for step in _items(case.get("steps")) for assertion in _items(_mapping(step).get("assertions"))]
        rows.append({
            "runId": result.get("runId"), "mode": result.get("mode"),
            "sourceCommit": environment.get("sourceCommit"), "model": environment.get("model"),
            "modelConfigured": environment.get("modelConfigured"), "modelCallsObserved": _model_calls(result),
            "caseModelCallsObserved": _case_model_calls(case),
            "caseId": case.get("id"), "title": case.get("title"), "category": case.get("category"),
            "status": case.get("status"), "reason": case.get("reason", ""),
            "stepCount": len(_items(case.get("steps"))),
            "passedAssertions": sum(_mapping(item).get("passed") is True for item in assertions),
            "failedAssertions": sum(_mapping(item).get("passed") is False for item in assertions),
            "humanReview": case.get("humanReview", []), "sydneyClock": environment.get("sydneyClock"),
        })
    return _csv_rows(headers, rows)


def _turns_csv(result: dict) -> str:
    environment = _mapping(result.get("environment"))
    headers = ["runId", "mode", "modelConfigured", "modelCallsObserved", "caseModelCallsObserved", "caseId", "caseStatus", "step", "op", "status", "reason", "durationMs", "input", "kokoReply", "httpStatus", "fieldChanges", "fields", "tripContext", "planningFields", "nextField", "question", "options", "quickReplies", "missingImportantFields", "completed", "total", "derivedReady", "pendingPreferences", "tripPreferences", "travelDna", "memoriesBefore", "memoriesAfter", "memoryDelta", "assertions", "request", "response"]
    rows = []
    for case in _items(result.get("cases")):
        case = _mapping(case)
        for index, step in enumerate(_items(case.get("steps")), 1):
            step = _mapping(step)
            session = _session(step)
            before, after = _mapping(step.get("before")), _mapping(step.get("after"))
            row = {
                "runId": result.get("runId"), "mode": result.get("mode"),
                "modelConfigured": environment.get("modelConfigured"), "modelCallsObserved": _model_calls(result),
                "caseModelCallsObserved": _case_model_calls(case),
                "caseId": case.get("id"), "caseStatus": case.get("status"),
                "step": step.get("index", index), "op": step.get("op"), "status": step.get("status"),
                "reason": step.get("reason", ""), "durationMs": step.get("durationMs"),
                "input": step.get("input"), "kokoReply": _reply(step, session),
                "httpStatus": _mapping(step.get("response")).get("httpStatus"),
                "fieldChanges": step.get("fieldChanges"),
                "quickReplies": _last_assistant(session).get("quickReplies"),
                "derivedReady": after.get("derivedReady"),
                "memoriesBefore": before.get("memories"), "memoriesAfter": after.get("memories"),
                "memoryDelta": after.get("memoryDelta"), "assertions": step.get("assertions"),
                "request": step.get("request"), "response": step.get("response"),
            }
            row.update({key: session.get(key) for key in ("fields", "tripContext", "planningFields", "nextField", "question", "options", "missingImportantFields", "completed", "total", "pendingPreferences", "tripPreferences", "travelDna")})
            rows.append(row)
    return _csv_rows(headers, rows)


def _write(path: Path, content: str) -> None:
    # Avoid leaving a half-written result when an interrupted run is resumed.
    with tempfile.NamedTemporaryFile(mode="w", encoding="utf-8", newline="", dir=path.parent, delete=False) as stream:
        temporary = Path(stream.name)
        try:
            stream.write(content)
            stream.flush()
            os.fsync(stream.fileno())
        except BaseException:
            temporary.unlink(missing_ok=True)
            raise
    try:
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


def write_reports(result: dict, output_dir: Path) -> dict[str, str]:
    """Write JSON, a standalone HTML report and two CSV evidence tables."""
    output_dir = Path(output_dir).resolve()
    output_dir.mkdir(parents=True, exist_ok=True)
    safe = redact(result)
    paths = {
        "json": output_dir / "run.json", "html": output_dir / "report.html",
        "summary_csv": output_dir / "summary.csv", "turns_csv": output_dir / "turns.csv",
    }
    contents = {
        "json": _pretty(safe) + "\n", "html": _html(safe),
        "summary_csv": _summary_csv(safe), "turns_csv": _turns_csv(safe),
    }
    for kind, path in paths.items():
        _write(path, contents[kind])
    return {kind: str(path) for kind, path in paths.items()}
