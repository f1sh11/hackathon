import copy
import csv
import io
import json
from pathlib import Path
import tempfile
import unittest

from tools.koko_api.report import redact, write_reports


def example():
    return {
        "schemaVersion": 1, "runId": "test-report", "mode": "fixture",
        "startedAt": "2026-10-26T00:00:00Z", "finishedAt": "2026-10-26T00:00:01Z",
        "environment": {"sourceCommit": "abc123", "model": "fixture", "modelConfigured": True,
                        "promptHashes": {"dialogue": "sha256-example"}, "sydneyClock": "2026-10-26T11:00:00+11:00"},
        "cases": [{
            "id": "01", "title": "信息复用", "category": "reuse", "status": "PASS",
            "humanReview": ["追问是否自然？"],
            "steps": [{
                "index": 1, "op": "turn", "status": "PASS", "input": "去悉尼玩三天", "durationMs": 5,
                "request": {"method": "POST", "path": "/pet/sessions/s/turn", "body": {"message": "去悉尼玩三天", "requestId": "synthetic-uuid"}},
                "response": {"httpStatus": 200, "events": [{"type": "reply", "text": "咱们先看看海边？"}]},
                "before": {"session": {"fields": {}}, "memories": []},
                "after": {"session": {"fields": {"destination": "悉尼", "days": "3"}, "planningFields": {"days": "3"},
                                      "tripContext": {"days": "3"}, "nextField": "date", "question": "何时出发？", "options": ["未确定"],
                                      "missingImportantFields": ["日期"], "completed": 2, "total": 7,
                                      "messages": [{"role": "assistant", "text": "咱们先看看海边？", "quickReplies": [{"id": "q", "text": "先看海边"}]}],
                                      "pendingPreferences": [], "tripPreferences": [], "travelDna": [],
                                      "futureServerField": {"still": "kept"}}, "memories": [], "derivedReady": False, "memoryDelta": 0},
                "fieldChanges": {"days": {"before": None, "after": "3"}},
                "assertions": [{"passed": True, "path": "session.fields.days", "message": "days equals 3", "expected": "3", "actual": "3"}],
            }],
        }],
        "totals": {"PASS": 1, "FAIL": 0, "NOT_RUN": 0},
    }


class ReportTests(unittest.TestCase):
    def reports(self, data):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        return {key: Path(path).read_text(encoding="utf-8") for key, path in write_reports(data, Path(directory.name)).items()}

    def test_renders_state_and_round_evidence_without_claiming_ui_success(self):
        reports = self.reports(example())
        self.assertEqual(set(reports), {"json", "html", "summary_csv", "turns_csv"})
        self.assertIn("固定桩模型", reports["html"])
        self.assertIn("不代表真实模型表现", reports["html"])
        self.assertIn("不是接口返回的 canGenerate", reports["html"])
        self.assertIn("不代表手机按钮已出现或可点击", reports["html"])
        self.assertIn("亲切程度和追问自然度需要人工评阅", reports["html"])
        self.assertIn("咱们先看看海边？", reports["html"])
        self.assertIn("missingImportantFields", reports["html"])
        self.assertIn("/memories", reports["html"])
        row = next(csv.DictReader(io.StringIO(reports["turns_csv"])))
        self.assertEqual(json.loads(row["fields"]), {"destination": "悉尼", "days": "3"})
        self.assertEqual(json.loads(row["fieldChanges"]), {"days": {"after": "3", "before": None}})
        self.assertEqual(row["derivedReady"], "false")
        self.assertEqual(row["completed"], "2")
        self.assertEqual(row["total"], "7")

    def test_html_escapes_untrusted_reply_metadata_and_attributes(self):
        data = example()
        attack = '<script>alert("bad")</script><img src=x onerror=evil()>'
        data["runId"] = attack
        data["cases"][0]["title"] = attack
        data["cases"][0]["steps"][0]["response"]["events"][0]["text"] = attack
        rendered = self.reports(data)["html"]
        self.assertNotIn("<script", rendered)
        self.assertNotIn("<img", rendered)
        self.assertIn("&lt;script&gt;", rendered)
        self.assertIn("&quot;bad&quot;", rendered)

    def test_csv_formula_cells_are_not_executable_and_multiline_roundtrips(self):
        data = example()
        data["cases"][0]["title"] = ' \t=HYPERLINK("https://example.invalid")'
        data["cases"][0]["steps"][0]["input"] = '@SUM(1,2)\n下一行,"引号"'
        reports = self.reports(data)
        summary = next(csv.DictReader(io.StringIO(reports["summary_csv"])))
        turn = next(csv.DictReader(io.StringIO(reports["turns_csv"])))
        self.assertTrue(summary["title"].startswith("'"))
        self.assertEqual(turn["input"], '\'@SUM(1,2)\n下一行,"引号"')
        # JSON is evidence, not an executable spreadsheet; retain exact original input.
        self.assertEqual(json.loads(reports["json"])["cases"][0]["steps"][0]["input"], data["cases"][0]["steps"][0]["input"])

    def test_credentials_are_redacted_in_every_format_without_mutating_input(self):
        data = example()
        data["environment"]["NVIDIA_API_KEY"] = "nvapi-private-value"
        request = data["cases"][0]["steps"][0]["request"]
        request["headers"] = {"Authorization": "Bearer private-token", "X-SydneyBuddy-Access-Code": "team-code", "Cookie": "session=private"}
        data["notes"] = "https://user:password@example.invalid/x?token=query-private; Password=database-secret; pwd='quoted secret'; access-code=code-private; nvapi-secret-other"
        original = copy.deepcopy(data)
        reports = self.reports(data)
        for content in reports.values():
            for secret in ("nvapi-private-value", "private-token", "team-code", "session=private", "user:password", "database-secret", "quoted secret", "query-private", "code-private", "nvapi-secret-other"):
                self.assertNotIn(secret, content)
        self.assertEqual(data, original)
        decoded = json.loads(reports["json"])
        self.assertEqual(decoded["environment"]["NVIDIA_API_KEY"], "[REDACTED]")
        self.assertTrue(decoded["environment"]["modelConfigured"])
        self.assertEqual(decoded["cases"][0]["steps"][0]["request"]["body"]["requestId"], "synthetic-uuid")

    def test_not_run_and_unknown_server_fields_remain_visible_and_do_not_pass(self):
        data = example()
        data["mode"] = "live"
        data["environment"]["modelConfigured"] = False
        data["cases"][0].update(status="NOT_RUN", reason="模型未配置", steps=[])
        data["totals"] = {"PASS": 999}  # Render counts from cases, not stale totals.
        reports = self.reports(data)
        self.assertIn("live 环境但真实模型未配置", reports["html"])
        self.assertIn("模型未配置", reports["html"])
        self.assertNotIn("999", reports["html"])
        summary = next(csv.DictReader(io.StringIO(reports["summary_csv"])))
        self.assertEqual(summary["status"], "NOT_RUN")
        self.assertEqual(summary["stepCount"], "0")
        self.assertEqual(list(csv.DictReader(io.StringIO(reports["turns_csv"]))), [])
        preserved = self.reports(example())
        self.assertIn("futureServerField", preserved["json"])
        self.assertIn("futureServerField", preserved["html"])

    def test_absent_response_and_memory_reads_stay_unknown_not_empty_success(self):
        data = {"mode": "fixture", "cases": [{"id": "broken", "status": "FAIL", "steps": [{"op": "get", "status": "FAIL", "reason": "connection refused"}]}]}
        reports = self.reports(data)
        self.assertIn("connection refused", reports["html"])
        self.assertIn("未提供／未读取", reports["html"])
        self.assertIn("后端完整性（工具推导）：未知", reports["html"])
        row = next(csv.DictReader(io.StringIO(reports["turns_csv"])))
        self.assertEqual(row["derivedReady"], "未提供")
        self.assertEqual(row["memoriesAfter"], "未提供")

    def test_empty_report_and_redaction_of_inline_bearer_are_supported(self):
        reports = self.reports({})
        self.assertIn("模型模式未提供", reports["html"])
        self.assertEqual(list(csv.DictReader(io.StringIO(reports["summary_csv"]))), [])
        self.assertEqual(redact("authentication failed: Bearer secret-token"), "authentication failed: Bearer [REDACTED]")

    def test_unexecuted_assertion_is_not_mislabeled_as_failure(self):
        data = example()
        data["cases"][0]["steps"][0]["assertions"] = [{"passed": None, "path": "session.fields", "message": "not evaluated after timeout"}]
        rendered = self.reports(data)["html"]
        self.assertIn('<span class="badge not-run">NOT_RUN</span></td><td>session.fields', rendered)

    def test_unconfigured_live_api_pass_does_not_claim_real_model_execution(self):
        data = example()
        data["mode"] = "live"
        data["environment"].update(modelConfigured=False, modelCallsObserved=0)
        data["cases"][0]["modelTrace"] = {"totalCalls": 0, "calls": []}
        data["cases"][0]["steps"][0]["op"] = "patch"
        original = copy.deepcopy(data)
        reports = self.reports(data)
        self.assertIn("仅执行不依赖模型的接口检查，其余未运行，未使用桩回复", reports["html"])
        self.assertIn("已观测模型调用数：0", reports["html"])
        self.assertNotIn("真实模型（live）", reports["html"])
        self.assertEqual(json.loads(reports["json"]), original)
        self.assertEqual(data, original)
        summary = next(csv.DictReader(io.StringIO(reports["summary_csv"])))
        turn = next(csv.DictReader(io.StringIO(reports["turns_csv"])))
        for row in (summary, turn):
            self.assertEqual(row["modelConfigured"], "false")
            self.assertEqual(row["modelCallsObserved"], "0")
            self.assertEqual(row["caseModelCallsObserved"], "0")
        self.assertEqual(summary["status"], "PASS")

    def test_observed_call_count_uses_trace_not_initial_host_counter(self):
        data = example()
        data["environment"]["modelCalls"] = 0
        data["cases"][0]["modelTrace"] = {"totalCalls": 2, "calls": [{"kind": "json"}, {"kind": "json"}]}
        reports = self.reports(data)
        self.assertIn("已观测模型调用数：2", reports["html"])
        summary = next(csv.DictReader(io.StringIO(reports["summary_csv"])))
        self.assertEqual(summary["modelCallsObserved"], "2")
        self.assertEqual(summary["caseModelCallsObserved"], "2")
        self.assertEqual(json.loads(reports["json"])["environment"]["modelCalls"], 0)


if __name__ == "__main__":
    unittest.main()
