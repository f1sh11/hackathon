import contextlib
import copy
import importlib.util
import io
import json
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[1] / "demo_agent.py"
SPEC = importlib.util.spec_from_file_location("headless_demo", SCRIPT)
demo = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(demo)
EXAMPLES = SCRIPT.parents[1] / "input_examples/headless"


class BackendDouble:
    """Transport double only. These tests do not prove live model integration."""
    def __init__(self, reviews=None, failure=None, old=False):
        self.calls = []
        self.reviews = list(reviews or [])
        self.failure = failure
        self.old = old

    def call(self, path, body=None):
        self.calls.append((path, copy.deepcopy(body)))
        if path == "/health":
            return {"status": "ok"} if self.old else {"planningAgentConfigured": True, "planningAgentProvider": "test_double"}
        if path == "/plan":
            if self.failure:
                raise self.failure
            return {"days": [{"stops": []}], "profile": body}
        if path == "/agents/feasibility/reviews":
            return self.reviews.pop(0)
        raise AssertionError("Unexpected API call: " + path)


class HeadlessDemoTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.output = Path(self.temporary.name) / "run"
        self.log = demo.RunLog(self.output, "sample", stream=io.StringIO())

    def case(self, name="ordinary-day"):
        return demo.load_case(EXAMPLES / (name + ".json"), "2026-10-28")

    def test_normal_case_revises_and_checks_fresh_result(self):
        case = self.case()
        original = copy.deepcopy(case)
        result = demo.run_sample(case, self.log, 2)
        self.assertEqual("pass", result["status"])
        self.assertEqual(1, result["revisions"])
        self.assertIn("opera", result["draft"]["selectedIds"])
        self.assertNotIn("mca", result["draft"]["selectedIds"])
        self.assertFalse(result["review"]["canConfirm"])
        self.assertFalse(result["review"]["modelReviewCompleted"])
        self.assertEqual(original, case)
        first = json.loads((self.output / "review-1.json").read_text())
        self.assertEqual("daily_budget_exceeded", first["issues"][0]["code"])

    def test_changing_budget_changes_computed_output(self):
        case = self.case()
        case["profile"]["budget"] = 100
        result = demo.run_sample(case, self.log, 2)
        self.assertEqual("pass", result["status"])
        self.assertEqual(0, result["revisions"])
        self.assertIn("mca", result["draft"]["selectedIds"])
        self.assertEqual(25, result["review"]["estimatedCost"])

    def test_required_paid_places_block_without_changing_limits(self):
        case = self.case("low-budget")
        result = demo.run_sample(case, self.log, 2)
        self.assertEqual("blocked", result["status"])
        self.assertEqual(5, result["draft"]["profile"]["budget"])
        self.assertEqual(set(case["profile"]["mustVisit"]), set(result["draft"]["selectedIds"]))

    def test_missing_routes_never_get_times_distances_or_approval(self):
        result = demo.run_sample(self.case("route-unavailable"), self.log, 2)
        self.assertEqual("needs_info", result["status"])
        self.assertEqual([], result["draft"]["routeLegs"])
        self.assertIsNone(result["draft"]["finishMinute"])
        self.assertIsNone(result["draft"]["stops"][0]["arrivalMinute"])
        self.assertIsNone(result["review"]["estimatedWalkingSteps"])
        self.assertFalse(result["review"]["canConfirm"])

    def test_repair_limit_is_enforced(self):
        result = demo.run_sample(self.case(), self.log, 0)
        self.assertEqual("blocked", result["status"])
        self.assertFalse((self.output / "draft-2.json").exists())

    def test_live_handoff_revises_with_original_hard_requirements(self):
        case = self.case()
        original = copy.deepcopy(case)
        reviews = [
            {"decision": "revise", "issues": [{"code": "daily_budget_exceeded", "actual": 25, "limit": 20}],
             "handoff": {"targetAgent": "itinerary_planner"}, "nextAgent": "itinerary_planner"},
            {"decision": "pass", "issues": [], "canConfirm": True, "modelReviewCompleted": True},
        ]
        client = BackendDouble(reviews)
        result = demo.run_live(case, self.log, 2, client, "demo")
        self.assertEqual("pass", result["status"])
        plans = [body for path, body in client.calls if path == "/plan"]
        self.assertEqual(2, len(plans))
        for field in ("budget", "mustVisit", "date", "maxSteps", "startMinute", "endMinute"):
            self.assertEqual(case["profile"][field], plans[1][field])
        self.assertIn("daily_budget_exceeded", plans[1]["request"])
        requests = [body for path, body in client.calls if path == "/agents/feasibility/reviews"]
        self.assertEqual([1, 2], [x["planRevision"] for x in requests])
        self.assertEqual(requests[0]["planId"], requests[1]["planId"])
        expected_requirements = copy.deepcopy(case["profile"])
        expected_requirements["interests"] = ["\u98ce\u666f", "\u827a\u672f"]
        self.assertEqual(expected_requirements, requests[1]["requirements"])
        self.assertEqual(expected_requirements["interests"], plans[1]["interests"])
        self.assertEqual("en", plans[1]["language"])
        self.assertEqual(original, case)
        self.assertFalse(any(path == "/trips" or path.endswith("/confirm") for path, _ in client.calls))

    def test_live_missing_evidence_stops_without_blind_retry(self):
        client = BackendDouble([{"decision": "needs_info", "canConfirm": False, "issues": [{"code": "walking_evidence_missing"}]}])
        result = demo.run_live(self.case(), self.log, 2, client, "demo")
        self.assertEqual("needs_info", result["status"])
        self.assertEqual(1, sum(path == "/plan" for path, _ in client.calls))

    def test_live_never_falls_back_to_sample_on_failure(self):
        client = BackendDouble(failure=demo.ApiError(503, "Provider unavailable"))
        result = demo.run_live(self.case(), self.log, 2, client, "demo")
        self.assertEqual("unavailable", result["status"])
        self.assertNotIn("draft", result)
        self.assertFalse((self.output / "draft-1.json").exists())

    def test_live_refuses_false_pass_and_old_backend(self):
        for client in (BackendDouble([{"decision": "pass", "canConfirm": True, "modelReviewCompleted": False}]), BackendDouble(old=True)):
            with self.subTest(client=client), self.assertRaises(demo.DemoError):
                demo.run_live(self.case(), self.log, 2, client, "demo")

    def test_client_rejects_insecure_remote_addresses_and_redirects(self):
        for url in ("http://example.com", "https://user:password@example.com", "https://example.com?token=secret"):
            with self.subTest(url=url), self.assertRaises(demo.DemoError):
                demo.ApiClient(url, self.log.id, "direct", 10)
        self.assertIsNone(demo.NoRedirect().redirect_request(None, None, 302, "", {}, "https://elsewhere.example"))

    def test_existing_output_is_not_overwritten(self):
        with self.assertRaises(FileExistsError):
            demo.RunLog(self.output, "sample")

    def test_cli_exports_results_with_unambiguous_sample_label(self):
        output = Path(self.temporary.name) / "cli"
        with contextlib.redirect_stdout(io.StringIO()):
            code = demo.main(["--input", str(EXAMPLES / "ordinary-day.json"), "--output-dir", str(output)])
        self.assertEqual(0, code)
        result = json.loads((output / "result.json").read_text())
        self.assertEqual("sample", result["mode"])
        self.assertFalse(result["saved"])
        self.assertIn("OFFLINE REHEARSAL", result["notice"])
        events = [json.loads(line) for line in (output / "events.jsonl").read_text().splitlines()]
        self.assertEqual(1, len({x["run_id"] for x in events}))
        self.assertTrue(any(x["status"] == "revise" for x in events))
        self.assertFalse((output / "backend-events.json").exists())


if __name__ == "__main__":
    unittest.main()
