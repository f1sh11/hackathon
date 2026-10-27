# Headless Command-Line Demo

`scripts/demo_agent.py` uses only the Python 3.9+ standard library. No iOS, Xcode, or third-party Python packages are required. The default mode is offline and needs no credentials. The script never saves a trip: it does not call `/trips` or a review's `/confirm` endpoint.

## Run the Three Offline Scenarios

Run these commands from the repository root. On Windows, replace `python3` with `python` if needed:

```sh
python3 scripts/demo_agent.py --input input_examples/headless/ordinary-day.json
python3 scripts/demo_agent.py --input input_examples/headless/low-budget.json
python3 scripts/demo_agent.py --input input_examples/headless/route-unavailable.json
```

| Input | Expected offline result | Behavior |
| --- | --- | --- |
| `ordinary-day.json` | `pass` after one revision | An optional paid visit exceeds the A$20 budget. The policy removes it and the checker recomputes the result. |
| `low-budget.json` | `blocked` | All three visits are required, but their sample cost exceeds A$5. Required visits and the budget are preserved; the user must resolve the conflict. |
| `route-unavailable.json` | `needs_info` | Simulated missing evidence leaves route legs empty and arrival times and walking steps unknown. No route or approval is fabricated. |

Dates default to tomorrow in Sydney. Use `--date 2026-10-28` to select an explicit date. Some Windows Python installations lack IANA timezone data; supply `--date` in that case without installing additional packages. Change `profile.budget`, `mustVisit`, the daily time window, or the step limit to see the result recomputed. No pre-recorded final answers are read at runtime.

**Offline mode is a workflow rehearsal, not the production agent system.** It uses a separate, simple Python selection policy and rule checker. Prices and the 15-minute / 600-meter directional legs are synthetic inputs, not verified venue prices or measured routes. No C# backend, LLM, Zetaris, Meterless, or map service is called. An offline `pass` means only that these sample checks passed; `canConfirm` and `modelReviewCompleted` remain false. Offline logs cannot prove real model or sponsor integration. Offline rehearsal supports one day only.

Example descriptions, requests, and interest labels are in English, and each profile requests `language: en`. In live mode, the client maps supported English interest labels to the backend's existing category values at the API boundary. The original input and user constraints are preserved. Source records and backend responses may retain their existing localization.

## Connect to the Real Backend

Follow the repository README to start PostgreSQL, Node 24, and the .NET API from this checkout. Configure both the selected planning model and the independent review model on the backend. `/health` must include `planningAgentConfigured`; an older service reporting only `status=ok` is insufficient. The script does not start or replace services or enable paid-service settings.

```sh
python3 scripts/demo_agent.py --mode live --input input_examples/headless/ordinary-day.json
```

Here, `live` means real HTTP requests to the backend. **It does not mean all travel data is real-time.** The default `--data-mode demo` still selects the backend's sample data mode, while `useAgent=true` requests the actual planning model. Select the backend's existing `assisted` or `live` data mode with `--data-mode`; its configuration and paid-service policies still apply.

To select a configured Zetaris source:

```sh
python3 scripts/demo_agent.py --mode live --data-source zetaris-cloud --input input_examples/headless/ordinary-day.json
```

Place IDs, costs, and model choices can differ between sources. The three offline expectations are not fixed answers for real services. `preferredIds` and `sampleRouteFailure` are offline-only settings. Live mode submits the profile and does not use the sample switch to fabricate a service outage.

The live orchestration performs these steps:

1. Call `GET /health` to check backend compatibility and planning-model configuration.
2. Call `POST /plan` with the user requirements and `useAgent=true` to obtain an unsaved draft.
3. Call `POST /agents/feasibility/reviews` for an independent review, retaining the original requirements as constraints.
4. On `revise`, when the handoff target is `itinerary_planner`, append the issue codes and values to the next planning request. Budget, required places, dates, time limits, and step limits are preserved. Increment `planRevision`; keep `requirementsRevision` at 1.
5. Allow at most two repair rounds. Stop on `needs_info` or `unavailable`. A `pass` also requires `canConfirm=true` and `modelReviewCompleted=true`. Even a successful draft is not automatically saved.
6. Export `/planning/runs/{run_id}` using the same `X-Planning-Run` value. Client-side request events alone are not proof that a model executed.

### Current Route-Evidence Limitations

The independent reviewer currently expects MapKit walking observations whose direction, distance, duration, and timestamp match the itinerary. Default `/plan` sample distances are not such evidence, and Google live routes cannot automatically be treated as MapKit observations. **Live mode may therefore return `needs_info`; a successful real review loop without iOS is not yet guaranteed.**

If genuine MapKit observations already exist, are still within their six-hour validity window, and cover the candidate legs, supply their JSON array:

```sh
python3 scripts/demo_agent.py --mode live --input input_examples/headless/ordinary-day.json --walking-evidence /absolute/path/to/current-mapkit-observations.json
```

Entries use the existing `walkingRoutes` contract: `from`, `to`, `meters`, `seconds`, and `fetchedAt`. Do not generate random distances, refresh old timestamps, or relabel Valhalla results as Apple observations. This option cannot be combined with Google `--data-mode live`.

For the ordinary live case to pass without any MapKit dependency, the **backend** route-evidence tool still needs to query a configured route service, preserve its real provenance, and recheck the itinerary. This script exposes that gap rather than fabricating evidence. Missing model configuration, upstream timeouts, and HTTP 422 responses are recorded without falling back to an offline success. A 422 is not automatically described as a budget conflict.

## Artifacts and Exit Codes

Each run creates a new `.demo-runs/<run_id>/` directory. Use `--output-dir <new-directory>` to choose another location; existing results are never overwritten. Artifacts may contain user requirements and unsaved drafts. The default output directory is Git-ignored.

- `input.json`: this run's input, including the resolved travel date.
- `draft-1.json`, `review-1.json`, and subsequent revisions: per-round drafts and reviews.
- `events.jsonl`: client or offline events also printed to the terminal, with mode, run ID, and repair attempt.
- `backend-events.json`: exported only when real backend trace retrieval succeeds; preserves the backend's actual roles, model/rule distinction, and call results.
- `result.json`: final status, mode, data source, unsaved status, and failure details when applicable.

Exit code `0` means the checks passed in the selected mode. Code `2` means blocked constraints, missing evidence, or a rejected planning request. Code `1` means a configuration, communication, or input error. The last two offline scenarios intentionally return a nonzero code.

Remote backends require HTTPS. An optional access token is read from `SYDNEYBUDDY_DEMO_TOKEN` and sent only over HTTPS, never passed as a command-line argument or printed. Redirects are rejected, and service response bodies are omitted from error logs.

## Prepare Submission Examples

The script does not create pre-recorded outputs for submission. After a real run, inspect `result.json` and `backend-events.json`, remove private information, and manually select relevant artifacts for `output_examples/`. Record the run date, service mode, model, and data sources. Failure artifacts can demonstrate failure handling. Do not relabel an offline `pass` as real agent success.

Run the checks with:

```sh
python3 -m unittest discover -s scripts/tests -p test_headless_demo.py
```

Tests use offline policies and HTTP transport doubles to verify preserved constraints, bounded repair, rejection of missing evidence, and no sample fallback in live mode. They do not prove live NVIDIA, Meterless, or Zetaris connectivity.
