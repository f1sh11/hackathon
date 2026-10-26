#!/usr/bin/env python3
"""Inspect Koko through the actual App API in an attested isolated test host."""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from koko_api.client import ApiClient, HarnessError, TestSession, redact
from koko_api.runner import CaseRunner, DEFAULT_CREATE, run_batch


def json_object(text):
    try:
        result = json.loads(text)
        if not isinstance(result, dict):
            raise ValueError("expected a JSON object")
        return result
    except (ValueError, TypeError) as exc:
        raise argparse.ArgumentTypeError(str(exc)) from exc


def present(response, *, full=False):
    if full:
        print(json.dumps(redact(response), ensure_ascii=False, indent=2))
        return
    if response.get("error"):
        print("ERROR:", response["error"])
    session = response.get("session") or response.get("data")
    if isinstance(session, dict) and "messages" in session:
        print("Session:", session["id"])
        latest = next((m for m in reversed(session["messages"]) if m.get("role") == "assistant"), {})
        print("Koko:", latest.get("text", ""))
        print("Fields:", json.dumps(session.get("fields", {}), ensure_ascii=False))
        print("Missing:", json.dumps(session.get("missingImportantFields", []), ensure_ascii=False))
        for index, option in enumerate(latest.get("quickReplies") or [], 1):
            print(f"  {index}. {option['text']} [{option.get('intent', '')}]")
        for preference in session.get("pendingPreferences", []):
            print("Pending:", preference.get("key"), "=", preference.get("value"), "id=" + preference["id"])
    elif session is not None:
        print(json.dumps(redact(session), ensure_ascii=False, indent=2))


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--url", default="http://127.0.0.1:8897")
    parser.add_argument("--state", type=Path, default=Path(".koko-api-state.json"))
    parser.add_argument("--timeout", type=float, default=120)
    parser.add_argument("--language", choices=["zh-Hans", "zh-Hant", "en"], default="zh-Hans")
    parser.add_argument("--json", action="store_true", help="print the full actual API response")
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("metadata")
    create = sub.add_parser("create")
    create.add_argument("--personal", action="store_true")
    create.add_argument("--fields", type=json_object, default={})
    resume = sub.add_parser("resume")
    resume.add_argument("session_id")
    sub.add_parser("show")
    send = sub.add_parser("send")
    send.add_argument("text")
    send.add_argument("--request-id")
    send.add_argument("--field")
    send.add_argument("--value")
    choice = sub.add_parser("choose")
    group = choice.add_mutually_exclusive_group(required=True)
    group.add_argument("--intent")
    group.add_argument("--index", type=int, help="1-based displayed option number")
    patch = sub.add_parser("patch")
    patch.add_argument("fields", type=json_object)
    memory = sub.add_parser("memory")
    memory.add_argument("key")
    memory.add_argument("decision", choices=["remember", "trip-only", "ignore"])
    memory.add_argument("--value")
    sub.add_parser("memories")
    sub.add_parser("complete")
    sub.add_parser("retry")
    sub.add_parser("abandon", help="archive an unresolved local request; does not undo a backend operation")
    sub.add_parser("cleanup", help="delete only IDs recorded in this state file's ownership ledger")
    sub.add_parser("chat", help="interactive turns, /pick N, /show, /retry, /abandon, /memories, /quit")
    batch = sub.add_parser("batch")
    batch.add_argument("--cases", type=Path, default=Path(__file__).parent / "koko_api/cases.json")
    batch.add_argument("--out", type=Path, required=True)
    batch.add_argument("--mode", choices=["fixture", "live"])
    batch.add_argument("--case", action="append", dest="case_ids")
    args = parser.parse_args(argv)
    try:
        client = ApiClient(args.url, args.timeout, language=args.language)
        meta = client.verify_environment()
        if args.command == "metadata":
            present({"data": meta}, full=True)
            return 0
        if args.command == "batch":
            result, paths = run_batch(client, args.state, args.cases, args.out, mode=args.mode, selected=args.case_ids)
            print(json.dumps({"totals": result["totals"], "reports": paths}, ensure_ascii=False, indent=2))
            return 1 if result["totals"]["FAIL"] else (3 if result["totals"]["NOT_RUN"] else 0)
        ledger = TestSession(client, args.state)
        if args.command == "create":
            response = ledger.create({**DEFAULT_CREATE, "companionVersion": 0 if args.personal else 1, "fields": args.fields})
        elif args.command == "resume":
            response = ledger.activate(args.session_id, adopt=True)
        elif args.command == "cleanup":
            results = ledger.cleanup()
            failures = [r for r in results if r.get("error") or r.get("httpStatus", 500) >= 400]
            present({"data": {"results": results, "failedCleanupRequests": len(failures)}}, full=True)
            return 1 if failures else 0
        elif args.command == "abandon":
            present({"data": {"archivedRequest": ledger.abandon_pending(), "backendOperationUndone": False}}, full=True)
            return 0
        elif args.command == "chat":
            if not ledger.state.get("activeSession"):
                present(ledger.create())
            print("Free text sends a real API turn. Commands: /pick N, /show, /retry, /abandon, /memories, /quit")
            while True:
                try:
                    text = input("you> ").strip()
                except (EOFError, KeyboardInterrupt):
                    print()
                    break
                if not text:
                    continue
                if text == "/quit":
                    break
                try:
                    if text == "/show":
                        response = client.request("GET", "/pet/sessions/" + ledger.session_id)
                    elif text == "/retry":
                        response = ledger.retry()
                    elif text == "/abandon":
                        response = {"data": {"archivedRequest": ledger.abandon_pending(), "backendOperationUndone": False}}
                    elif text == "/memories":
                        response = client.request("GET", "/memories")
                    elif text.startswith("/pick "):
                        current = client.request("GET", "/pet/sessions/" + ledger.session_id)
                        response = ledger.choose(current["data"], index=int(text[6:]) - 1)
                    elif text.startswith("/"):
                        print("Use the documented CLI subcommands for patch/memory decisions; unknown chat command was not sent")
                        continue
                    else:
                        response = ledger.turn({"message": text})
                    present(response)
                except (HarnessError, ValueError) as exc:
                    print("ERROR:", redact(str(exc)))
            return 0
        else:
            current = client.request("GET", "/pet/sessions/" + ledger.session_id)
            before = client.request("GET", "/memories").get("data", [])
            runner = CaseRunner(client, ledger, Path("."))
            runner.session = current.get("data", {})
            if args.command == "show":
                response = current
            elif args.command == "memories":
                response = client.request("GET", "/memories")
            else:
                step = {"op": {"send": "turn", "choose": "choice"}.get(args.command, args.command)}
                if args.command == "send":
                    step["text"] = args.text
                    for source, target in (("request_id", "requestId"), ("field", "field"), ("value", "value")):
                        if getattr(args, source) is not None:
                            step[target] = getattr(args, source)
                elif args.command == "choose":
                    step.update(intent=args.intent, index=args.index - 1 if args.index is not None else None)
                elif args.command == "patch":
                    step["fields"] = args.fields
                elif args.command == "memory":
                    step.update(key=args.key, consent=args.decision == "remember", dismiss=args.decision == "ignore")
                    if args.value is not None:
                        step["value"] = args.value
                _, response = runner.action(step)
            after = client.request("GET", "/memories").get("data", [])
            ledger.remember_owned(before, after)
        present(response, full=args.json)
        return 1 if response.get("error") or response.get("httpStatus", 200) >= 400 else 0
    except (HarnessError, ValueError, OSError) as exc:
        print("Harness error:", redact(str(exc)), file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
