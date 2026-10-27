#!/usr/bin/env python3
"""Headless rehearsal or real API orchestration. Standard library only; never saves a trip."""
import argparse
import copy
import datetime as dt
import json
import math
import os
import sys
import urllib.error
import urllib.parse
import urllib.request
import uuid
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
# The API still uses legacy category values. Keep examples and offline output in English.
API_INTERESTS = {
    "scenery": "\u98ce\u666f", "art": "\u827a\u672f", "food": "\u7f8e\u98df",
    "nature": "\u81ea\u7136", "culture": "\u6587\u5316", "city": "\u57ce\u5e02",
}
SAMPLE_NOTICE = (
    "OFFLINE REHEARSAL: deterministic Python policies and synthetic travel data. "
    "No production C# planner, model, Zetaris, Meterless or real route service was called. "
    "A sample pass is not a production approval or a verified travel itinerary."
)


class DemoError(Exception):
    pass


class ApiError(DemoError):
    def __init__(self, status, message):
        self.status = status
        super().__init__(message)


def now():
    return dt.datetime.now(dt.timezone.utc).isoformat()


def read_json(path):
    with Path(path).open(encoding="utf-8") as source:
        return json.load(source)


def load_case(path, date=None):
    case = read_json(path)
    if not isinstance(case, dict) or not isinstance(case.get("profile"), dict):
        raise DemoError("Input must contain a profile object.")
    profile = case["profile"]
    if date:
        profile["date"] = date
    elif profile.get("date") == "tomorrow":
        from zoneinfo import ZoneInfo, ZoneInfoNotFoundError
        try:
            profile["date"] = (dt.datetime.now(ZoneInfo("Australia/Sydney")).date()
                               + dt.timedelta(days=1)).isoformat()
        except ZoneInfoNotFoundError as error:
            raise DemoError("Sydney timezone data is unavailable; supply an explicit --date YYYY-MM-DD.") from error
    try:
        dt.date.fromisoformat(profile["date"])
        for key in ("budget", "maxSteps", "startMinute", "endMinute", "days"):
            value = profile[key]
            if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value):
                raise ValueError(key)
        if (profile["budget"] < 0 or profile["maxSteps"] < 500
                or not 0 <= profile["startMinute"] < profile["endMinute"] <= 1439
                or not 1 <= profile["days"] <= 15):
            raise ValueError("range")
        for key in ("maxSteps", "startMinute", "endMinute", "days"):
            if not isinstance(profile[key], int):
                raise ValueError(key)
        for key in ("mustVisit", "interests"):
            if not isinstance(profile.get(key, []), list) or not all(isinstance(x, str) for x in profile.get(key, [])):
                raise ValueError(key)
        if not isinstance(profile.get("request", ""), str) or len(profile.get("request", "")) > 1500:
            raise ValueError("request")
    except (KeyError, TypeError, ValueError) as error:
        raise DemoError("Invalid date, budget, steps, daily time window or day count in profile.") from error
    return case


class RunLog:
    def __init__(self, output, mode, stream=None):
        self.id = str(uuid.uuid4())
        self.mode = mode
        self.output = Path(output) if output else ROOT / ".demo-runs" / self.id
        self.output.mkdir(mode=0o700, parents=True, exist_ok=False)
        self.stream = stream if stream is not None else sys.stdout
        self.sequence = 0

    def event(self, actor, action, status, attempt=0, target=None, **evidence):
        self.sequence += 1
        row = dict(timestamp=now(), run_id=self.id, sequence=self.sequence,
                   mode=self.mode, actor=actor, action=action, status=status,
                   attempt=attempt, target=target, evidence=evidence)
        encoded = json.dumps(row, ensure_ascii=False)
        fd = os.open(self.output / "events.jsonl", os.O_WRONLY | os.O_CREAT | os.O_APPEND, 0o600)
        with os.fdopen(fd, "a", encoding="utf-8") as destination:
            destination.write(encoded + "\n")
        print(encoded, file=self.stream, flush=True)

    def save(self, name, value):
        fd = os.open(self.output / name, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        with os.fdopen(fd, "w", encoding="utf-8") as destination:
            json.dump(value, destination, ensure_ascii=False, indent=2, allow_nan=False)
            destination.write("\n")


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, message, headers, url):
        return None


class ApiClient:
    def __init__(self, base, run_id, data_source, timeout):
        parts = urllib.parse.urlsplit(base)
        if (not parts.hostname or parts.username or parts.password or parts.query or parts.fragment
                or not (parts.scheme == "https" or parts.scheme == "http"
                        and parts.hostname in ("localhost", "127.0.0.1", "::1"))):
            raise DemoError("Backend must use HTTPS, or HTTP on a loopback address, without credentials/query/fragment.")
        self.base = base.rstrip("/")
        self.timeout = timeout
        self.headers = {"Content-Type": "application/json", "X-Planning-Run": run_id,
                        "X-Travel-Data-Source": data_source}
        token = os.environ.get("SYDNEYBUDDY_DEMO_TOKEN", "")
        if token:
            if parts.scheme != "https":
                raise DemoError("The backend access token may only be sent over HTTPS.")
            self.headers["Authorization"] = "Bearer " + token
        self.opener = urllib.request.build_opener(NoRedirect())

    def call(self, path, body=None):
        payload = None if body is None else json.dumps(body, allow_nan=False).encode()
        if payload is not None and len(payload) > 250000:
            raise DemoError("Request exceeds the backend's 250,000-byte limit.")
        request = urllib.request.Request(self.base + path, data=payload, headers=self.headers,
                                         method="GET" if body is None else "POST")
        try:
            with self.opener.open(request, timeout=self.timeout) as response:
                encoded = response.read(2000001)
                if len(encoded) > 2000000:
                    raise DemoError("Backend response exceeds the demo client's 2 MB limit.")
                return json.loads(encoded)
        except urllib.error.HTTPError as error:
            # Keep provider URLs, bodies and credentials out of the exported trace.
            raise ApiError(error.code, "Backend returned HTTP %d; inspect its local diagnostics." % error.code) from error
        except (urllib.error.URLError, TimeoutError, OSError) as error:
            raise DemoError("Cannot reach the backend, or the request timed out. Check its address and startup.") from error
        except (ValueError, UnicodeError) as error:
            raise DemoError("Backend returned invalid JSON.") from error


def sample_proposal(case, catalog, previous, issues):
    """A deliberately simple policy, not a model or the production planner."""
    required = case["profile"].get("mustVisit", [])
    ids = list(dict.fromkeys(case.get("preferredIds", []) + required)) if previous is None else previous["selectedIds"][:]
    if any(ident not in catalog for ident in ids):
        raise DemoError("Sample input contains an unknown place ID.")
    if previous is not None:
        optional = [ident for ident in ids if ident not in required]
        if optional:
            remove = max(optional, key=lambda ident: catalog[ident]["cost"])
            ids.remove(remove)
    profile = copy.deepcopy(case["profile"])
    clock = profile["startMinute"]
    stops = []
    for ident in ids:
        place = copy.deepcopy(catalog[ident])
        clock += 15  # Synthetic route input, NOT measured from coordinates.
        place["arrivalMinute"] = None if case.get("sampleRouteFailure", False) else clock
        clock += place["visitMinutes"]
        place["departureMinute"] = None if case.get("sampleRouteFailure", False) else clock
        stops.append(place)
    clock += 15 if stops else 0
    return dict(profile=profile, selectedIds=ids, stops=stops,
                finishMinute=None if case.get("sampleRouteFailure", False) else clock,
                routeEvidenceAvailable=not case.get("sampleRouteFailure", False),
                evidenceSource="synthetic fixture; every directional leg is 15 min / 600 m",
                routeLegs=[dict(minutes=15, meters=600, source="synthetic_fixture")
                           for _ in range(len(stops) + 1)] if not case.get("sampleRouteFailure", False) else [])


def sample_review(draft):
    """Recompute from stops and route inputs. No pre-recorded decisions or final answers."""
    profile = draft["profile"]
    issues = []
    cost = sum(round(stop["cost"] * 100) for stop in draft["stops"]) / 100
    if cost > profile["budget"]:
        issues.append(dict(code="daily_budget_exceeded", severity="blocking", actual=cost, limit=profile["budget"]))
    if not draft["stops"]:
        issues.append(dict(code="no_stops", severity="blocking"))
    if not set(profile.get("mustVisit", [])).issubset(draft["selectedIds"]):
        issues.append(dict(code="must_visit_missing", severity="blocking"))
    if draft["finishMinute"] is not None and draft["finishMinute"] > profile["endMinute"]:
        issues.append(dict(code="return_time_exceeded", severity="blocking"))
    if not draft["routeEvidenceAvailable"]:
        issues.append(dict(code="walking_evidence_missing", severity="needs_info"))
        steps = None
    else:
        steps = math.ceil(sum(leg["meters"] for leg in draft["routeLegs"]) / 0.75)
        if steps > profile["maxSteps"]:
            issues.append(dict(code="daily_steps_exceeded", severity="blocking", actual=steps, limit=profile["maxSteps"]))
    decision = "revise" if any(x["severity"] == "blocking" for x in issues) else "needs_info" if issues else "pass"
    return dict(decision=decision, canConfirm=False, modelReviewCompleted=False,
                sampleChecksPassed=decision == "pass", issues=issues,
                estimatedCost=cost, estimatedWalkingSteps=steps,
                warnings=["All prices, opening assumptions and routes are synthetic; not a travel recommendation."])


def run_sample(case, log, max_repairs):
    if case["profile"]["days"] != 1:
        raise DemoError("The offline rehearsal supports one day only; use live mode for the production planner.")
    catalog = {x["id"]: x for x in read_json(ROOT / "input_examples/headless/sample-data.json")["places"]}
    previous, issues = None, []
    for attempt in range(max_repairs + 1):
        log.event("SamplePlannerPolicy", "generate" if attempt == 0 else "revise", "running", attempt,
                  target="SampleRuleChecker", actor_kind="deterministic_sample", issue_codes=[x["code"] for x in issues])
        draft = sample_proposal(case, catalog, previous, issues)
        log.save("draft-%d.json" % (attempt + 1), draft)
        review = sample_review(draft)
        log.save("review-%d.json" % (attempt + 1), review)
        log.event("SampleRuleChecker", "review", review["decision"], attempt,
                  target="SamplePlannerPolicy" if review["decision"] == "revise" else "DemoRunner",
                  actor_kind="deterministic_sample", issues=review["issues"])
        if review["decision"] != "revise":
            return dict(status=review["decision"], draft=draft, review=review, revisions=attempt)
        if previous is not None and draft["selectedIds"] == previous["selectedIds"]:
            return dict(status="blocked", draft=draft, review=review, revisions=attempt,
                        reason="Required places conflict with the limits; ask the user to change requirements. Limits were not relaxed.")
        previous, issues = draft, review["issues"]
    return dict(status="blocked", draft=draft, review=review, revisions=max_repairs,
                reason="Repair limit reached. User requirements were kept unchanged.")


def repair_request(original, review):
    profile = copy.deepcopy(original)
    details = [dict(code=x.get("code"), actual=x.get("actual"), limit=x.get("limit"))
               for x in review.get("issues", [])[:6] if isinstance(x, dict)]
    feedback = "\nIndependent review requests revision: " + json.dumps(details, ensure_ascii=False)
    feedback += ". Keep all original requirements and required places; revise optional choices or order."
    request = profile.get("request", "")
    if len(request) + len(feedback) > 1500:
        raise DemoError("Review feedback cannot fit without truncating the original request. Shorten the input request explicitly.")
    profile["request"] = request + feedback
    return profile


def run_live(case, log, max_repairs, client, data_mode, walking_evidence=None):
    health = client.call("/health")
    if not isinstance(health, dict) or "planningAgentConfigured" not in health:
        raise DemoError("The running backend is too old for this demo. Start this checkout's current .NET API.")
    log.event("DemoRunner", "preflight", "success", planning_provider=health.get("planningAgentProvider"),
              planning_configured=health.get("planningAgentConfigured"), data_mode=data_mode)
    if not health.get("planningAgentConfigured"):
        raise DemoError("Configure the backend's selected planning model before running live mode.")
    requirements = copy.deepcopy(case["profile"])
    requirements["interests"] = [API_INTERESTS.get(value, value) for value in requirements.get("interests", [])]
    profile = copy.deepcopy(requirements)
    plan_id = "headless-" + log.id
    for attempt in range(max_repairs + 1):
        request = copy.deepcopy(profile)
        request.update(mode=data_mode, useAgent=True)
        if walking_evidence is not None:
            request["walkingRoutes"] = walking_evidence
        log.event("DemoRunner", "request_plan", "running", attempt, target="PlanningAgent")
        try:
            draft = client.call("/plan", request)
        except ApiError as error:
            # A 422 can mean invalid input, missing configuration OR infeasible constraints.
            # Do not claim it proves a budget conflict or invent a successful draft.
            return dict(status="planning_rejected" if error.status == 422 else "unavailable",
                        stage="planning", httpStatus=error.status, revisions=attempt,
                        reason=str(error))
        if not isinstance(draft, dict) or not isinstance(draft.get("days"), list) or not draft["days"]:
            raise DemoError("Planning API returned no structured days.")
        draft.pop("previousAdjustment", None)
        log.save("draft-%d.json" % (attempt + 1), draft)
        review = client.call("/agents/feasibility/reviews", dict(
            planId=plan_id, planRevision=attempt + 1, requirementsRevision=1,
            trip=draft, requirements=requirements))
        if not isinstance(review, dict) or review.get("decision") not in ("pass", "revise", "needs_info", "unavailable"):
            raise DemoError("Review API returned an invalid decision.")
        log.save("review-%d.json" % (attempt + 1), review)
        log.event("DemoRunner", "received_review", review["decision"], attempt,
                  target=review.get("nextAgent"), issues=review.get("issues", []),
                  model_review_completed=review.get("modelReviewCompleted", False))
        if review["decision"] == "pass":
            if review.get("canConfirm") is not True or review.get("modelReviewCompleted") is not True:
                raise DemoError("Backend reported pass without a completed independent model review; refusing approval.")
            return dict(status="pass", draft=draft, review=review, revisions=attempt)
        if review["decision"] != "revise":
            return dict(status=review["decision"], draft=draft, review=review, revisions=attempt,
                        reason="Missing evidence or model availability must be resolved before approval.")
        if attempt < max_repairs:
            if review.get("handoff", {}).get("targetAgent") != "itinerary_planner":
                return dict(status="needs_info", draft=draft, review=review, revisions=attempt,
                            reason="The handoff requires user clarification, not an automatic planning retry.")
            profile = repair_request(requirements, review)
    return dict(status="blocked", draft=draft, review=review, revisions=max_repairs,
                reason="Repair limit reached. No trip was saved and no hard requirement was relaxed.")


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", required=True, type=Path)
    parser.add_argument("--mode", choices=("sample", "live"), default="sample")
    parser.add_argument("--backend", default="http://127.0.0.1:8787")
    parser.add_argument("--data-source", choices=("direct", "zetaris", "zetaris-cloud"), default="direct")
    parser.add_argument("--data-mode", choices=("demo", "assisted", "live"), default="demo")
    parser.add_argument("--walking-evidence", type=Path, help="Optional current, genuine MapKit directional observations; never synthesize these in live mode.")
    parser.add_argument("--date", help="Travel date YYYY-MM-DD; otherwise examples use tomorrow in Sydney.")
    parser.add_argument("--max-repairs", type=int, choices=range(3), default=2)
    parser.add_argument("--timeout", type=int, default=240)
    parser.add_argument("--output-dir", type=Path, help="New directory only; existing results are never overwritten.")
    args = parser.parse_args(argv)
    log, client, result = None, None, None
    try:
        case = load_case(args.input, args.date)
        if not 1 <= args.timeout <= 600:
            raise DemoError("Timeout must be between 1 and 600 seconds.")
        if args.mode == "sample" and (args.walking_evidence or args.data_source != "direct" or args.data_mode != "demo"):
            raise DemoError("Sample mode cannot use live route files, data-source overrides or paid data modes.")
        log = RunLog(args.output_dir, args.mode)
        log.save("input.json", case)
        notice = SAMPLE_NOTICE if args.mode == "sample" else (
            "REAL BACKEND CALLS: model/data provenance depends on backend configuration. "
            "data-mode=demo still uses sample place/route inputs. No trip will be saved."
        )
        log.event("DemoRunner", "run_started", "running", notice=notice,
                  case=case.get("name", args.input.stem), max_repairs=args.max_repairs)
        if args.mode == "sample":
            result = run_sample(case, log, args.max_repairs)
        else:
            client = ApiClient(args.backend, log.id, args.data_source, args.timeout)
            evidence = read_json(args.walking_evidence) if args.walking_evidence else None
            if evidence is not None and not isinstance(evidence, list):
                raise DemoError("Walking evidence must be a JSON array of genuine observations.")
            if args.data_mode == "live" and evidence is not None:
                raise DemoError("Google live data cannot be mixed with MapKit observations.")
            result = run_live(case, log, args.max_repairs, client, args.data_mode, evidence)
        result.update(runId=log.id, mode=args.mode, notice=notice, saved=False,
                      dataMode="synthetic_fixture" if args.mode == "sample" else args.data_mode,
                      dataSource="synthetic_fixture" if args.mode == "sample" else args.data_source)
    except (DemoError, OSError, ValueError, KeyError, TypeError) as error:
        message = str(error) if isinstance(error, DemoError) else "Invalid input file or output directory; check JSON and file permissions."
        if log is None:
            print(message, file=sys.stderr)
            return 1
        result = dict(status="unavailable", mode=args.mode, runId=log.id, saved=False, reason=message)
        log.event("DemoRunner", "run_error", "failed", reason=message)
    finally:
        if client is not None and log is not None:
            try:
                events = client.call("/planning/runs/" + log.id)
                if not isinstance(events, dict) or not isinstance(events.get("events"), list):
                    raise DemoError("Backend trace response was invalid.")
                log.save("backend-events.json", events)
                log.event("DemoRunner", "export_backend_trace", "success", event_count=len(events["events"]))
            except (DemoError, OSError):
                log.event("DemoRunner", "export_backend_trace", "unavailable",
                          reason="No backend trace could be exported; client events are not proof of model collaboration.")
    log.save("result.json", result)
    log.event("DemoRunner", "run_finished", result["status"], output_directory=str(log.output), saved=False)
    print("\nResult: %s | mode: %s | files: %s" % (result["status"], args.mode, log.output))
    return 0 if result["status"] == "pass" else 2 if result["status"] in ("blocked", "needs_info", "planning_rejected") else 1


if __name__ == "__main__":
    sys.exit(main())
