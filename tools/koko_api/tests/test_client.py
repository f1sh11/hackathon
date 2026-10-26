import json
import tempfile
import threading
import unittest
import uuid
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

from tools.koko_api.client import ApiClient, HarnessError, ProtocolError, TestSession, parse_ndjson
from tools.koko_api.runner import ABSENT, at_path, check_assertion


META = {"isolated": True, "environmentId": "unit-test-host", "mode": "fixture", "schemaPrefix": "koko_cli_", "fixtureApiVersion": 1}


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *args):
        pass

    def do_GET(self):
        self.server.get_paths.append(self.path)
        if self.path == "/redirect":
            self.send_response(302)
            self.send_header("Location", "/must-not-follow")
            self.end_headers()
            return
        if self.path.endswith("/gone"):
            self.send_error(404)
            return
        body = json.dumps({"id": self.path.rsplit("/", 1)[-1]} if self.path.startswith("/pet/sessions/") else META).encode()
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_POST(self):
        body = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
        self.server.requests.append(body)
        output = self.server.output.replace(b"__SESSION_ID__", self.path.split("/")[-2].encode())
        self.send_response(200)
        self.send_header("Content-Type", "application/x-ndjson; charset=utf-8")
        self.send_header("Content-Length", str(len(output)))
        self.end_headers()
        # Fragment UTF-8 characters and NDJSON boundaries across real TCP writes.
        for index in range(0, len(output), 2):
            self.wfile.write(output[index:index + 2])
            self.wfile.flush()


class ClientTests(unittest.TestCase):
    def setUp(self):
        self.server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.server.requests = []
        self.server.get_paths = []
        self.server.output = b'{"type":"done","session":{"id":"__SESSION_ID__"}}\n'
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.client = ApiClient(f"http://127.0.0.1:{self.server.server_port}")
        self.client.verify_environment()
        self.temporary = tempfile.TemporaryDirectory()

    def tearDown(self):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join()
        self.temporary.cleanup()

    def ledger(self):
        ledger = TestSession(self.client, Path(self.temporary.name) / "state.json")
        identifier = uuid.uuid4().hex
        ledger.state["sessions"] = [identifier]
        ledger.state["activeSession"] = identifier
        ledger.save()
        return ledger

    def test_fragmented_unicode_stream_waits_for_done(self):
        self.server.output = ('{"type":"reply","text":"咱们慢慢走"}\n'
                              '{"type":"done","session":{"id":"ok","fields":{"days":"3"}}}\n').encode()
        response = self.client.request("POST", "/pet/sessions/test/turn", {"message": "三天"}, stream=True)
        self.assertEqual(response["terminal"], "done")
        self.assertEqual(response["events"][0]["text"], "咱们慢慢走")
        self.assertEqual(response["session"]["fields"]["days"], "3")

    def test_http_200_error_is_not_a_completed_turn(self):
        self.server.output = '{"type":"error","error":"模型未配置"}\n'.encode()
        response = self.client.request("POST", "/pet/sessions/test/turn", {}, stream=True)
        self.assertEqual(response["httpStatus"], 200)
        self.assertEqual(response["terminal"], "error")
        self.assertIsNone(response["session"])

    def test_redirect_cannot_escape_the_attested_local_api(self):
        response = self.client.request("GET", "/redirect")
        self.assertEqual(response["httpStatus"], 302)
        self.assertTrue(response["error"])
        self.assertNotIn("/must-not-follow", self.server.get_paths)

    def test_interrupted_stream_persists_request_before_retry(self):
        ledger = self.ledger()
        self.server.output = b'{"type":"reply","text":"partial"}\n'
        body = {"message": "test evidence", "requestId": str(uuid.uuid4()), "questionId": "q", "optionId": "o"}
        with self.assertRaises(ProtocolError):
            ledger.turn(body)
        resumed = TestSession(self.client, ledger.path)
        self.assertEqual(resumed.state["pendingRequest"]["body"], body)
        self.server.output = b'{"type":"done","session":{"id":"__SESSION_ID__"}}\n'
        resumed.retry()
        self.assertEqual(self.server.requests, [body, body])
        self.assertIsNone(resumed.state["pendingRequest"])

    def test_language_is_part_of_the_exact_persisted_retry_body(self):
        self.client.language = "en"
        ledger = self.ledger()
        self.server.output = b'{"type":"reply","text":"partial"}\n'
        with self.assertRaises(ProtocolError):
            ledger.turn({"message": "same input"})
        original = ledger.state["pendingRequest"]["body"].copy()
        self.assertEqual(original["language"], "en")
        self.client.language = "zh-Hant"
        self.server.output = b'{"type":"done","session":{"id":"__SESSION_ID__"}}\n'
        ledger.retry()
        self.assertEqual(self.server.requests, [original, original])

    def test_environment_mismatch_does_not_overwrite_state(self):
        ledger = self.ledger()
        original = ledger.path.read_bytes()
        self.client.metadata = {**META, "environmentId": "different"}
        with self.assertRaises(HarnessError):
            TestSession(self.client, ledger.path)
        self.assertEqual(ledger.path.read_bytes(), original)

    def test_unresolved_request_cannot_be_silently_replaced(self):
        ledger = self.ledger()
        self.server.output = b'{"type":"reply","text":"partial"}\n'
        original = {"message": "first requirement", "requestId": str(uuid.uuid4())}
        with self.assertRaises(ProtocolError):
            ledger.turn(original)
        with self.assertRaisesRegex(HarnessError, "unresolved"):
            ledger.turn({"message": "different requirement"})
        with self.assertRaisesRegex(HarnessError, "unresolved"):
            ledger.create()
        self.assertEqual(self.server.requests, [original])
        self.assertEqual(ledger.state["pendingRequest"]["body"], original)
        ledger.abandon_pending()
        self.assertEqual(ledger.state["archivedRequests"][0]["body"], original)

    def test_failed_restoration_never_retries_into_the_active_other_session(self):
        ledger = self.ledger()
        active = ledger.session_id
        ledger.state["pendingRequest"] = {"sessionId": "gone", "body": {"message": "A", "requestId": str(uuid.uuid4())}}
        # Simulate an API 404 while retaining the other active session B.
        ledger.activate = lambda identifier: {"httpStatus": 404, "error": "missing"}
        with self.assertRaisesRegex(HarnessError, "another session"):
            ledger.retry()
        self.assertEqual(ledger.session_id, active)
        self.assertEqual(self.server.requests, [])

    def test_done_for_another_session_does_not_clear_the_journal(self):
        ledger = self.ledger()
        self.server.output = b'{"type":"done","session":{"id":"another-session"}}\n'
        with self.assertRaisesRegex(ProtocolError, "session ID"):
            ledger.turn({"message": "same owner"})
        self.assertIsNotNone(ledger.state["pendingRequest"])

    def test_cleanup_refuses_unowned_ids_before_network_mutation(self):
        ledger = self.ledger()
        with self.assertRaises(HarnessError):
            ledger.cleanup(memories=["unowned-record"])
        self.assertEqual(self.server.requests, [])

    def test_editor_option_is_not_fabricated_as_a_turn(self):
        ledger = self.ledger()
        session = {"messages": [{"id": "q", "role": "assistant", "quickReplies": [
            {"id": "o", "text": "修改日期", "intent": "details_time"}]}]}
        with self.assertRaisesRegex(HarnessError, "editor"):
            ledger.choose(session, index=0)
        self.assertEqual(self.server.requests, [])


class ProtocolAndAssertionTests(unittest.TestCase):
    def test_external_or_credential_urls_are_refused(self):
        for url in ["https://example.com", "http://user:secret@localhost:8897", "http://127.0.0.1:8897/?token=x", "http://localhost:8897/api"]:
            with self.subTest(url=url), self.assertRaises(HarnessError):
                ApiClient(url)

    def test_ordinary_app_backend_does_not_attest_isolation(self):
        client = ApiClient()
        calls = []
        client.request = lambda method, path: calls.append((method, path)) or {"httpStatus": 404, "data": {"error": "missing"}}
        with self.assertRaises(HarnessError):
            client.verify_environment()
        self.assertEqual(calls, [("GET", "/__koko_test__/metadata")])

    def test_incomplete_unknown_and_double_terminal_streams_are_rejected(self):
        for stream in [[b'{"type":"reply","text":"x"}\n'], [b'{"type":"unknown"}\n'],
                       [b'{"type":"done","session":{"id":"x"}}\n', b'{"type":"error","error":"late"}\n'],
                       [b'{"type":"error"}\n'], [b'{"type":"done","session":{}}\n']]:
            with self.subTest(stream=stream), self.assertRaises(ProtocolError):
                parse_ndjson(stream)

    def test_missing_is_not_equal_to_null_and_list_paths_work(self):
        snapshot = {"session": {"pendingPreferences": [{"conflictingMemoryIds": ["old"]}], "fields": {"days": "2", "destination": "悉尼"}}}
        self.assertIs(at_path(snapshot, "session.fields.date"), ABSENT)
        self.assertFalse(check_assertion({"path": "session.fields.date", "equals": None}, snapshot, {})["passed"])
        self.assertTrue(check_assertion({"path": "session.pendingPreferences.0.conflictingMemoryIds", "length": 1}, snapshot, {})["passed"])
        baseline = {"saved": {"fields": {"days": "3", "destination": "悉尼"}}}
        self.assertTrue(check_assertion({"path": "session.fields", "unchangedExcept": ["days"], "from": "saved.fields"}, snapshot, baseline)["passed"])


if __name__ == "__main__":
    unittest.main()
