"""Case execution and objective assertions over actual API responses."""
from __future__ import annotations

import copy
import datetime as dt
import hashlib
import json
import os
import subprocess
import time
import uuid
from pathlib import Path
from zoneinfo import ZoneInfo, ZoneInfoNotFoundError

from . import BASELINE_COMMIT
from .client import ApiClient, HarnessError, TestSession, TransportError, atomic_json, redact
from .report import write_reports

ABSENT = object()
DEFAULT_CREATE = {"profilingVersion": 2, "companionVersion": 1, "hasMetKoko": True, "nickname": ""}


def sydney_zone():
    try:
        return ZoneInfo("Australia/Sydney")
    except ZoneInfoNotFoundError as exc:
        raise HarnessError("Australia/Sydney timezone data is missing; install the tzdata package (Windows: py -m pip install tzdata)") from exc


def clock():
    now = dt.datetime.now(dt.timezone.utc)
    return {"utc": now.isoformat(), "sydney": now.astimezone(sydney_zone()).isoformat(),
            "timeZone": "Australia/Sydney", "source": "system"}


def at_path(value, path, default=ABSENT):
    for key in path.split(".") if path else []:
        if isinstance(value, dict) and key in value:
            value = value[key]
        elif isinstance(value, list) and key.isdigit() and int(key) < len(value):
            value = value[int(key)]
        else:
            return default
    return value


def substitute(value, context):
    if isinstance(value, str) and value.startswith("${") and value.endswith("}"):
        replacement = at_path(context, value[2:-1])
        if replacement is ABSENT:
            raise HarnessError("Unknown case reference: " + value)
        return copy.deepcopy(replacement)
    if isinstance(value, dict):
        return {k: substitute(v, context) for k, v in value.items()}
    if isinstance(value, list):
        return [substitute(v, context) for v in value]
    return value


def derived_ready(session):
    # This is only the backend response's completeness. It is NOT an App canGenerate flag.
    return (isinstance(session, dict) and session.get("missingImportantFields") == []
            and session.get("nextField") == "" and session.get("completed") == session.get("total") == 7)


def check_assertion(rule, snapshot, references):
    path = rule.get("path", "")
    actual = at_path(snapshot, path)
    comparisons = []
    expected = {k: v for k, v in rule.items() if k != "path"}
    if "absent" in rule:
        comparisons.append((actual is ABSENT) == rule["absent"])
    if "equals" in rule:
        comparisons.append(actual is not ABSENT and actual == rule["equals"])
    if "notEquals" in rule:
        comparisons.append(actual is not ABSENT and actual != rule["notEquals"])
    if "equalsRef" in rule:
        reference = at_path(references, rule["equalsRef"])
        comparisons.append(reference is not ABSENT and actual is not ABSENT and actual == reference)
    if "length" in rule:
        comparisons.append(isinstance(actual, (list, dict, str)) and len(actual) == rule["length"])
    for operation in ("contains", "notContains"):
        if operation in rule:
            present = isinstance(actual, (list, dict, str)) and rule[operation] in actual
            comparisons.append(present if operation == "contains" else actual is not ABSENT and not present)
    if "any" in rule:
        comparisons.append(isinstance(actual, list) and any(
            isinstance(item, dict) and all(at_path(item, k) == v for k, v in rule["any"].items()) for item in actual))
    if "all" in rule:
        comparisons.append(isinstance(actual, list) and all(
            isinstance(item, dict) and all(at_path(item, k) == v for k, v in rule["all"].items()) for item in actual))
    for operation, compare in (("gte", lambda a, b: a >= b), ("lte", lambda a, b: a <= b)):
        if operation in rule:
            comparisons.append(isinstance(actual, (int, float)) and compare(actual, rule[operation]))
    if "unchangedExcept" in rule:
        reference = at_path(references, rule["from"])
        excluded = set(rule["unchangedExcept"])
        comparisons.append(isinstance(actual, dict) and isinstance(reference, dict)
                           and {k: v for k, v in actual.items() if k not in excluded}
                           == {k: v for k, v in reference.items() if k not in excluded})
    if not comparisons:
        raise HarnessError("Assertion has no supported comparison: " + json.dumps(rule))
    return {"passed": all(comparisons), "path": path, "expected": expected,
            "actual": "<absent>" if actual is ABSENT else actual,
            "message": "matches the expected state" if all(comparisons) else "actual state differs from the expected condition"}


def changed_fields(before, after):
    result = {}
    for namespace in ("fields", "planningFields", "tripContext"):
        a = before.get("session", {}).get(namespace, {}) or {}
        b = after.get("session", {}).get(namespace, {}) or {}
        for key in sorted(set(a) | set(b)):
            if a.get(key, ABSENT) != b.get(key, ABSENT):
                result[namespace + "." + key] = {"before": a.get(key), "after": b.get(key)}
    return result


class CaseRunner:
    def __init__(self, client, ledger, output_dir):
        self.client, self.ledger, self.output_dir = client, ledger, Path(output_dir)
        self.refs = {}
        self.baseline_memories = []
        self.session = {}

    def memories(self):
        response = self.client.request("GET", "/memories")
        if response["httpStatus"] != 200 or not isinstance(response.get("data"), list):
            raise HarnessError("Cannot read persistent memory truth: " + str(response.get("error")))
        return response["data"]

    def snapshot(self, response=None):
        if self.ledger.state.get("activeSession"):
            current = self.client.request("GET", f"/pet/sessions/{self.ledger.session_id}")
            if current["httpStatus"] != 200:
                raise HarnessError("Cannot reload owned session: " + str(current.get("error")))
            self.session = current["data"]
        memories = self.memories()
        messages = self.session.get("messages", [])
        return {"session": copy.deepcopy(self.session), "memories": memories, "memoryCount": len(memories),
                "memoryDelta": len(memories) - len(self.baseline_memories),
                "userMessageCount": sum(m.get("role") == "user" for m in messages),
                "assistantMessageCount": sum(m.get("role") == "assistant" for m in messages),
                "messageIds": [m.get("id") for m in messages], "derivedReady": derived_ready(self.session),
                "lastStatus": (response or {}).get("httpStatus"),
                "lastEvent": (response or {}).get("terminal"), "error": (response or {}).get("error")}

    def action(self, step):
        op = step["op"]
        method, path, body, stream = "GET", "/pet/sessions/" + self.ledger.session_id, None, False
        if "sessionRef" in step:
            session_id = at_path(self.refs, step["sessionRef"])
            if session_id is ABSENT:
                raise HarnessError("Unknown saved session reference")
            self.ledger.activate(session_id)
            path = "/pet/sessions/" + self.ledger.session_id
        if op == "get":
            response = self.client.request(method, path)
        elif op == "patch":
            method, body = "PATCH", {"fields": step["fields"]}
            response = self.client.request(method, path, body)
        elif op == "turn":
            method, path, stream = "POST", path + "/turn", True
            body = {"message": step["text"], "requestId": step.get("requestId", str(uuid.uuid4()))}
            for name in ("field", "value", "questionId", "optionId"):
                if name in step:
                    body[name] = step[name]
            response = self.ledger.turn(body, timeout=step.get("timeout"))
        elif op == "choice":
            method, path, stream = "POST", path + "/turn", True
            response = self.ledger.choose(self.session, intent=step.get("intent"), index=step.get("index"))
            body = self.ledger.state["lastTurn"]["body"]
        elif op == "retry":
            method, stream = "POST", True
            response = self.ledger.retry(timeout=step.get("timeout"))
            body = self.ledger.state["lastTurn"]["body"]
            path = "/pet/sessions/" + self.ledger.session_id + "/turn"
        elif op == "new_session":
            method, path, body = "POST", "/pet/sessions", {**DEFAULT_CREATE, **step.get("body", {})}
            response = self.ledger.create(body)
        elif op == "memories":
            path = "/memories"
            response = self.client.request(method, path)
        elif op == "memory":
            method, path = "POST", path + "/memory"
            if step.get("repeat"):
                body = self.ledger.state.get("lastMemory")
                if not body:
                    raise HarnessError("No previous memory decision to repeat")
            else:
                candidate = next((p for p in self.session.get("pendingPreferences", []) if p.get("key") == step["key"]
                                  and ("value" not in step or p.get("value") == step["value"])), None)
                if not candidate:
                    raise HarnessError("No pending candidate for " + step["key"])
                body = {"candidateId": candidate["id"], "consent": step.get("consent", False),
                        "dismiss": step.get("dismiss", False),
                        "expectedConflicts": step.get("expectedConflicts", candidate.get("conflictingMemoryIds", []))}
            body = self.client.prepare_body(method, path, body)
            self.ledger.state["lastMemory"] = copy.deepcopy(body)
            self.ledger.save()
            response = self.client.request(method, path, body)
        elif op == "complete":
            method, path, body = "POST", path + "/profile/complete", {}
            response = self.client.request(method, path, body)
        elif op in {"edit_memory", "delete_memory"}:
            record = next((m for m in self.memories() if at_path(m, "profile.travelDna.key") == step["key"]
                           and m["id"] in self.ledger.state["memories"]), None)
            if not record:
                raise HarnessError("Memory edit/delete refused: no matching ledger-owned record")
            path = "/memories/" + record["id"]
            method = "PUT" if op == "edit_memory" else "DELETE"
            if op == "edit_memory":
                profile = copy.deepcopy(record["profile"])
                profile["travelDna"].update(value=step["value"], source="user_correction", evidence=step["value"], sourceMessageId="")
                body = {"consent": True, "summary": step["value"], "profile": profile}
            response = self.client.request(method, path, body)
        else:
            raise HarnessError("Unknown case operation: " + op)
        return copy.deepcopy(self.client.last_request), response

    def run_case(self, case, mode):
        result = {k: copy.deepcopy(case.get(k)) for k in ("id", "title", "category", "humanReview")}
        result.update(status="NOT_RUN", steps=[], reason="")
        meta = self.client.metadata
        if mode == "live" and case.get("fixtureOnly"):
            result["reason"] = "Deterministic fault injection is fixture-only; no live response was fabricated"
            return result
        if case.get("requiresModel", True) and not meta.get("modelConfigured"):
            result["reason"] = "Real NVIDIA model is not configured in this isolated host"
            return result
        now = dt.datetime.now(sydney_zone()).date()
        self.refs = {"sydneyToday": now.isoformat(), "sydneyTomorrow": (now + dt.timedelta(days=1)).isoformat(),
                     "futureDate": (now + dt.timedelta(days=30)).isoformat()}
        case = substitute(case, self.refs)
        owned_before = {k: set(self.ledger.state[k]) for k in ("sessions", "memories")}
        self.session = {}
        try:
            self.baseline_memories = self.memories()
            if self.baseline_memories:
                raise HarnessError("Case isolation requires an empty memory baseline; use a fresh test host or explicit ledger cleanup")
            if mode == "fixture":
                fixture = self.client.request("POST", "/__koko_test__/fixture", {
                    "caseId": case["id"], "responses": case.get("fixtures", [])})
                if fixture["httpStatus"] != 200:
                    raise HarnessError("Cannot load the exact-message model fixture")
            else:
                model_calls_before = self.client.request("GET", "/__koko_test__/fixture").get("data", {}).get("totalCalls", 0)
            create_body = {**DEFAULT_CREATE, **case.get("create", {})}
            created = self.ledger.create(create_body)
            if created["httpStatus"] != 200:
                raise HarnessError("Create failed: " + str(created.get("error")))
            self.session = created["data"]
            result["create"] = {"request": copy.deepcopy(self.client.last_request), "response": created}
            previous = self.snapshot(created)
            result["status"] = "PASS"
            for index, step in enumerate(case["steps"], 1):
                step = substitute(step, self.refs)
                before = copy.deepcopy(previous)
                trace = {"index": index, "op": step["op"], "input": step, "before": before,
                         "clock": clock(), "status": "FAIL", "assertions": []}
                started = time.monotonic()
                request_marker = self.client.request_serial
                request, response = None, None
                try:
                    try:
                        request, response = self.action(step)
                    except TransportError as exc:
                        pending = self.ledger.state.get("pendingRequest")
                        if pending and step["op"] in {"turn", "choice", "retry"}:
                            request = {"method": "POST", "path": "/pet/sessions/" + pending["sessionId"] + "/turn",
                                       "body": pending["body"], "requestId": pending["body"]["requestId"]}
                        if not step.get("expectedTransportError"):
                            raise
                        response = {"httpStatus": 0, "error": str(exc), "terminal": "transport_error", "events": []}
                    trace["request"], trace["response"] = request, response
                    after = self.snapshot(response)
                    self.ledger.remember_owned(before["memories"], after["memories"])
                    trace["after"] = after
                    trace["fieldChanges"] = changed_fields(before, after)
                    expected_error = bool(step.get("expectedError") or step.get("expectedTransportError") or step.get("expectedHttp", 200) >= 400)
                    actual_error = bool(response.get("error") or response["httpStatus"] >= 400)
                    checks = [{"passed": actual_error == expected_error, "path": "response.error",
                               "expected": expected_error, "actual": actual_error,
                               "message": "error/complete outcome matches expectation" if actual_error == expected_error else "unexpected error or unexpectedly successful response"}]
                    if not step.get("expectedTransportError"):
                        checks.append({"passed": response["httpStatus"] == step.get("expectedHttp", 200),
                                       "path": "response.httpStatus", "expected": step.get("expectedHttp", 200), "actual": response["httpStatus"]})
                    if step["op"] in {"turn", "choice", "retry"} and not expected_error:
                        checks.append({"passed": response.get("terminal") == "done", "path": "response.terminal",
                                       "expected": "done", "actual": response.get("terminal")})
                    references = {**self.refs, "before": before}
                    checks.extend(check_assertion(rule, after, references) for rule in step.get("assertions", []))
                    trace["assertions"] = checks
                    trace["status"] = "PASS" if all(c["passed"] for c in checks) else "FAIL"
                    if "saveAs" in step:
                        self.refs[step["saveAs"]] = copy.deepcopy(after)
                    previous = after
                except (HarnessError, ValueError, KeyError, IndexError, TypeError) as exc:
                    if request is None and self.client.request_serial > request_marker:
                        request = copy.deepcopy(self.client.last_request)
                    trace["request"], trace["response"] = request, response
                    trace["reason"] = redact(str(exc))
                    try:
                        trace["after"] = self.snapshot(response)
                        previous = trace["after"]
                    except HarnessError as refresh_error:
                        trace["reloadError"] = str(refresh_error)
                trace["durationMs"] = round((time.monotonic() - started) * 1000)
                trace["finishedClock"] = clock()
                result["steps"].append(trace)
                if trace["status"] != "PASS":
                    result["status"] = "FAIL"
            trace = self.client.request("GET", "/__koko_test__/fixture").get("data", {})
            if mode == "fixture":
                result["modelTrace"] = trace
                if result["modelTrace"].get("unexpectedCalls", 0):
                    result["status"] = "FAIL"
                    result["reason"] = "Fixture script had an unexpected model call; inspect modelTrace before attributing this to the App"
            else:
                calls = trace.get("calls", [])[model_calls_before:]
                result["modelTrace"] = {"mode": "live", "calls": calls, "totalCalls": len(calls)}
        except (HarnessError, ValueError, KeyError) as exc:
            result["status"] = "NOT_RUN" if not result["steps"] else "FAIL"
            result["reason"] = redact(str(exc))
        finally:
            cleanup_sessions = [s for s in self.ledger.state["sessions"] if s not in owned_before["sessions"]]
            cleanup_memories = [m for m in self.ledger.state["memories"] if m not in owned_before["memories"]]
            try:
                result["cleanup"] = self.ledger.cleanup(sessions=cleanup_sessions, memories=cleanup_memories)
                failures = [r for r in result["cleanup"] if r.get("error")]
                if failures:
                    result["cleanupWarning"] = "Some ledger-owned cleanup requests failed; no unowned records were removed"
            except HarnessError as exc:
                result["cleanupWarning"] = str(exc)
        return redact(result)


def git_revision(root):
    try:
        return subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip()
    except (OSError, subprocess.SubprocessError):
        return "unavailable"


def production_changes(root):
    try:
        return subprocess.check_output(["git", "status", "--porcelain", "--", "server-dotnet/SydneyBuddy.Api"],
                                       cwd=root, text=True).splitlines()
    except (OSError, subprocess.SubprocessError):
        return ["unavailable"]


def run_batch(client, state_path, case_path, output_dir, *, mode=None, selected=None):
    meta = client.verify_environment()
    mode = mode or meta["mode"]
    if mode != meta["mode"]:
        raise HarnessError("Requested mode differs from the isolated host's actual model mode")
    content = Path(case_path).read_bytes()
    manifest = json.loads(content)
    if manifest.get("version") != 1:
        raise HarnessError("Unsupported case manifest version")
    cases = [c for c in manifest["cases"] if not selected or c["id"] in selected]
    if not cases or selected and set(selected) != {c["id"] for c in cases}:
        raise HarnessError("Unknown or empty case selection")
    root = Path(__file__).resolve().parents[2]
    ledger = TestSession(client, state_path)
    hashes = {str(p.relative_to(root)): hashlib.sha256(p.read_bytes()).hexdigest()
              for p in sorted((root / "tools/koko_api").glob("*.py"))}
    hashes.update({str(p.relative_to(root)): hashlib.sha256(p.read_bytes()).hexdigest()
                   for p in sorted((root / "tools/koko_api/FixtureHost").glob("*.cs"))})
    entry = root / "tools/koko_api_cli.py"
    hashes[str(entry.relative_to(root))] = hashlib.sha256(entry.read_bytes()).hexdigest()
    result = {"schemaVersion": 1, "runId": str(uuid.uuid4()), "mode": mode, "startedAt": clock()["utc"],
              "environment": {**{k: v for k, v in meta.items() if k != "modelCalls"}, "modelCallsAtStart": meta.get("modelCalls"),
                              "sourceCommit": git_revision(root), "caseBaselineCommit": BASELINE_COMMIT,
                              "gitRevision": git_revision(root), "requestLanguage": client.language,
                              "productionWorkingTreeChanges": production_changes(root), "toolingHashes": hashes,
                              "databaseEngine": os.environ.get("KOKO_TEST_DB_ENGINE", "PostgreSQL (operator-provided isolated database)"),
                              "fixtureHash": hashlib.sha256(content).hexdigest(), "sydneyClock": clock()},
              "baseUrl": client.base_url, "cases": [], "totals": {"PASS": 0, "FAIL": 0, "NOT_RUN": 0}}
    runner = CaseRunner(client, ledger, output_dir)
    for case in cases:
        outcome = runner.run_case(case, mode)
        result["cases"].append(outcome)
        result["totals"][outcome["status"]] += 1
        result["environment"]["modelCallsObserved"] = sum(c.get("modelTrace", {}).get("totalCalls", 0) for c in result["cases"])
        result["finishedAt"] = clock()["utc"]
        write_reports(result, Path(output_dir))  # Checkpoint after every completed case, including failures.
        print(f"{outcome['status']:7} {outcome['id']} {outcome['title']}", flush=True)
    result["finishedAt"] = clock()["utc"]
    paths = write_reports(result, Path(output_dir))
    return result, paths
