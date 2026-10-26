"""Small standard-library HTTP client for the App's real JSON/NDJSON routes."""
from __future__ import annotations

import copy
import json
import os
import socket
import tempfile
import urllib.error
import urllib.parse
import urllib.request
import uuid
from pathlib import Path


class HarnessError(RuntimeError):
    pass


class TransportError(HarnessError):
    pass


class ProtocolError(HarnessError):
    pass


EDITOR_INTENTS = {
    "compose_name", "compose_idea", "details", "details_destination",
    "details_time", "details_people", "details_budget", "keep_draft",
}


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, message, headers, new_url):
        return None


def redact(value):
    """Never persist credential values, headers, or connection strings."""
    secrets = [os.environ.get(k, "") for k in (
        "KOKO_TEST_NVIDIA_API_KEY", "NVIDIA_API_KEY", "SYDNEYBUDDY_ACCESS_CODE",
        "KOKO_TEST_CONNECTION", "ConnectionStrings__SydneyBuddy",
    )]
    sensitive = {"authorization", "cookie", "set-cookie", "password", "token",
                 "apikey", "api_key", "accesscode", "access_code", "connectionstring"}
    if isinstance(value, dict):
        return {k: "[REDACTED]" if k.lower().replace("-", "_") in sensitive
                else redact(v) for k, v in value.items()}
    if isinstance(value, list):
        return [redact(v) for v in value]
    if isinstance(value, str):
        for secret in secrets:
            if len(secret) >= 4:
                value = value.replace(secret, "[REDACTED]")
    return value


def atomic_json(path: Path, data):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    fd, temporary = tempfile.mkstemp(prefix="." + path.name, dir=path.parent)
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as stream:
            os.chmod(temporary, 0o600)
            json.dump(redact(data), stream, ensure_ascii=False, indent=2)
            stream.write("\n")
        os.replace(temporary, path)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


def parse_ndjson(lines):
    events = []
    terminal = None
    for raw in lines:
        if not raw.strip():
            continue
        try:
            event = json.loads(raw)
        except (json.JSONDecodeError, UnicodeDecodeError) as exc:
            raise ProtocolError("Malformed NDJSON event; progress must be re-read before retry") from exc
        if not isinstance(event, dict) or event.get("type") not in {"reply", "done", "error"}:
            raise ProtocolError("Unknown NDJSON event")
        if terminal is not None:
            raise ProtocolError("Events received after the terminal NDJSON event")
        if event["type"] == "reply" and not isinstance(event.get("text"), str):
            raise ProtocolError("reply event has no text")
        if event["type"] == "error" and (not isinstance(event.get("error"), str) or not event["error"].strip()):
            raise ProtocolError("error event has no error message")
        if event["type"] == "done" and (not isinstance(event.get("session"), dict) or not event["session"].get("id")):
            raise ProtocolError("done event has no session")
        if event["type"] in {"done", "error"}:
            terminal = event["type"]
        events.append(event)
    if terminal is None:
        raise ProtocolError("Incomplete stream: no done/error event; HTTP 200 is not completion")
    last = events[-1]
    return {"events": events, "terminal": terminal, "session": last.get("session"),
            "error": last.get("error") if terminal == "error" else None}


class ApiClient:
    def __init__(self, base_url="http://127.0.0.1:8897", timeout=120, language=None):
        parsed = urllib.parse.urlsplit(base_url)
        if (parsed.scheme != "http" or parsed.hostname not in {"localhost", "127.0.0.1", "::1"}
                or parsed.username or parsed.password or parsed.query or parsed.fragment
                or parsed.path not in {"", "/"}):
            raise HarnessError("Only an isolated loopback test host is supported; cloud/shared App URLs are refused")
        self.base_url = base_url.rstrip("/")
        self.timeout = float(timeout)
        if language not in {None, "zh-Hans", "zh-Hant", "en"}:
            raise HarnessError("Unsupported interface language")
        self.language = language
        # Do not send loopback traffic through HTTP(S)_PROXY or ambient credentials.
        self.opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())
        self.metadata = None
        self.request_serial = 0
        self.last_request = None

    def prepare_body(self, method, path, body):
        body = copy.deepcopy(body)
        if self.language and method in {"POST", "PATCH"} and path.startswith("/pet/sessions") and isinstance(body, dict):
            body.setdefault("language", self.language)
        return body

    def request(self, method, path, body=None, *, stream=False, timeout=None):
        if not path.startswith("/") or path.startswith("//") or ".." in path or "?" in path:
            raise HarnessError("Expected a local API path")
        body = self.prepare_body(method, path, body)
        data = None if body is None else json.dumps(body, ensure_ascii=False).encode()
        request = urllib.request.Request(self.base_url + path, data=data, method=method,
                                        headers={"Content-Type": "application/json", "Accept": "application/x-ndjson" if stream else "application/json"})
        self.request_serial += 1
        self.last_request = redact({"method": method, "path": path, "body": copy.deepcopy(body),
                                    "requestId": body.get("requestId") if isinstance(body, dict) else None})
        try:
            with self.opener.open(request, timeout=self.timeout if timeout is None else float(timeout)) as response:
                status = response.status
                if stream:
                    if "application/x-ndjson" not in response.headers.get("Content-Type", ""):
                        raise ProtocolError("Turn response is not NDJSON")
                    result = parse_ndjson(response)
                else:
                    result = {"data": json.load(response)}
        except urllib.error.HTTPError as exc:
            raw = exc.read().decode("utf-8", errors="replace")
            try:
                data = json.loads(raw)
            except json.JSONDecodeError:
                data = {"error": raw[:2000]}
            error = data.get("error", raw[:2000]) if isinstance(data, dict) else str(data)
            return redact({"httpStatus": exc.code, "data": data,
                           "error": error or f"HTTP {exc.code}; redirects are not followed", "events": []})
        except (urllib.error.URLError, TimeoutError, socket.timeout, ConnectionError, OSError) as exc:
            raise TransportError(redact(f"{type(exc).__name__}: {exc}")) from exc
        return redact({"httpStatus": status, **result})

    def verify_environment(self):
        result = self.request("GET", "/__koko_test__/metadata")
        meta = result.get("data")
        if (result["httpStatus"] != 200 or not isinstance(meta, dict)
                or meta.get("isolated") is not True or not meta.get("environmentId")
                or meta.get("mode") not in {"fixture", "live"}
                or meta.get("schemaPrefix") != "koko_cli_" or meta.get("fixtureApiVersion") != 1):
            raise HarnessError("Test-environment attestation missing. Start the dedicated FixtureHost; no App data was modified")
        self.metadata = meta
        return meta


class TestSession:
    """Persistent ownership and pending-request ledger, bound to one isolated host."""
    def __init__(self, client: ApiClient, path: Path):
        self.client, self.path = client, Path(path)
        meta = client.metadata or client.verify_environment()
        if self.path.exists():
            self.state = json.loads(self.path.read_text(encoding="utf-8"))
            if (self.state.get("schemaVersion") != 1 or self.state.get("baseUrl") != client.base_url
                    or self.state.get("environmentId") != meta["environmentId"]):
                raise HarnessError("State belongs to another test environment; use its owned host/data directory or a new state file")
        else:
            self.state = {"schemaVersion": 1, "runId": str(uuid.uuid4()), "baseUrl": client.base_url,
                          "environmentId": meta["environmentId"], "sessions": [], "memories": [],
                          "activeSession": None, "pendingRequest": None, "lastTurn": None, "lastMemory": None}

    def save(self):
        atomic_json(self.path, self.state)

    @property
    def session_id(self):
        value = self.state.get("activeSession")
        if not value or value not in self.state["sessions"]:
            raise HarnessError("No owned active session; use create or resume")
        return value

    def activate(self, session_id, *, adopt=False):
        try:
            uuid.UUID(session_id)
        except (ValueError, TypeError) as exc:
            raise HarnessError("Invalid test session ID") from exc
        if session_id not in self.state["sessions"] and not adopt:
            raise HarnessError("Session is not in this tool's ownership ledger")
        response = self.client.request("GET", f"/pet/sessions/{session_id}")
        if response["httpStatus"] == 200:
            if response.get("data", {}).get("id") != session_id:
                raise ProtocolError("Restored session ID differs from the requested session")
            if session_id not in self.state["sessions"]:
                self.state["sessions"].append(session_id)
            self.state["activeSession"] = session_id
            self.save()
        return response

    def create(self, body=None):
        if self.state.get("pendingRequest"):
            raise HarnessError("An unresolved request is saved; retry or explicitly abandon it before creating another session")
        response = self.client.request("POST", "/pet/sessions", body or {
            "profilingVersion": 2, "companionVersion": 1, "hasMetKoko": True, "nickname": ""})
        if response["httpStatus"] == 200 and response.get("data", {}).get("id"):
            session_id = response["data"]["id"]
            self.state["sessions"].append(session_id)
            self.state["activeSession"] = session_id
            self.state["pendingRequest"] = None
            self.save()
        return response

    def turn(self, body, *, timeout=None):
        body = self.client.prepare_body("POST", "/pet/sessions/" + self.session_id + "/turn", body)
        if "requestId" not in body:
            body = {**body, "requestId": str(uuid.uuid4())}
        try:
            uuid.UUID(body["requestId"])
        except (ValueError, TypeError) as exc:
            raise HarnessError("requestId must be a UUID") from exc
        request = {"sessionId": self.session_id, "body": copy.deepcopy(body)}
        pending = self.state.get("pendingRequest")
        if pending and pending != request:
            raise HarnessError("An unresolved request is saved; retry or explicitly abandon it before sending different input")
        self.state["pendingRequest"] = request
        self.state["lastTurn"] = request
        self.save()  # Persist BEFORE HTTP; an interrupted process can retry exactly this request.
        response = self.client.request("POST", f"/pet/sessions/{self.session_id}/turn", body,
                                       stream=True, timeout=timeout)
        if response.get("terminal") == "done":
            if response["session"].get("id") != request["sessionId"]:
                raise ProtocolError("done session ID differs from the requested session; pending request retained")
            self.state["pendingRequest"] = None
        self.save()
        return response

    def retry(self, *, timeout=None):
        request = self.state.get("pendingRequest") or self.state.get("lastTurn")
        if not request:
            raise HarnessError("No saved request to retry")
        activated = self.activate(request["sessionId"])
        if activated["httpStatus"] != 200 or self.session_id != request["sessionId"]:
            raise HarnessError("Saved request session could not be restored; no retry was sent to another session")
        return self.turn(request["body"], timeout=timeout)

    def abandon_pending(self, reason="operator_explicitly_archived"):
        request = self.state.get("pendingRequest")
        if request:
            self.state.setdefault("archivedRequests", []).append({**copy.deepcopy(request), "reason": reason})
            self.state["pendingRequest"] = None
            self.save()
        return request

    def choose(self, session, *, intent=None, index=None):
        question = next((m for m in reversed(session.get("messages", [])) if m.get("role") == "assistant"), {})
        options = question.get("quickReplies") or []
        option = next((o for o in options if o.get("intent") == intent), None) if intent else (
            options[index] if isinstance(index, int) and 0 <= index < len(options) else None)
        if not option:
            raise HarnessError("Requested option is not in the current assistant question")
        if option.get("intent") in EDITOR_INTENTS:
            raise HarnessError("This option opens an App editor/input action; use patch or free text instead of a fabricated turn")
        return self.turn({"message": option["text"], "questionId": question["id"], "optionId": option["id"]})

    def remember_owned(self, before, after):
        previous = {m["id"] for m in before}
        for memory in after:
            if memory["id"] not in previous and memory["id"] not in self.state["memories"]:
                self.state["memories"].append(memory["id"])
        self.save()

    def cleanup(self, *, sessions=None, memories=None):
        results = []
        for kind, identifiers in (("memories", memories if memories is not None else list(self.state["memories"])),
                                  ("sessions", sessions if sessions is not None else list(self.state["sessions"]))):
            for identifier in identifiers:
                if identifier not in self.state[kind]:
                    raise HarnessError("Cleanup refused an ID not in the ownership ledger")
                path = f"/memories/{identifier}" if kind == "memories" else f"/pet/sessions/{identifier}"
                response = self.client.request("DELETE", path)
                results.append({"path": path, **response})
                if response["httpStatus"] in {200, 404, 422} and not response.get("error"):
                    self.state[kind].remove(identifier)
                    if kind == "sessions" and (self.state.get("pendingRequest") or {}).get("sessionId") == identifier:
                        self.abandon_pending("owned_test_session_explicitly_cleaned")
        if self.state.get("activeSession") not in self.state["sessions"]:
            self.state["activeSession"] = None
        self.save()
        return results
