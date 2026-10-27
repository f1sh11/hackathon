# Single Meterless itinerary agent

Branch: `feat/meterless-itinerary-agent`. Runtime is not installed or started by this change. No production/Oracle settings are changed.

## Selected architecture: Meterless engines + cloud model

The preferred mode for this branch is now **`meterless-cloud`**. It uses the official vendored **Meterless H-MEM** reference for confirmed preference retrieval, the project's existing **World Model application implementation** for route-state projections and PostgreSQL events, and **NVIDIA NIM** for model inference. It does not require Meterless Runtime or a Meterless-hosted cloud service. The older `meterless` Runtime provider remains available as an explicit alternative; `nvidia` retains the legacy rank-only path.

```text
App -> ASP.NET Core
        -> selected local/Zetaris attractions, dining, lodging and daily weather
        -> H-MEM: retrieve user-confirmed preferences, never auto-save inferred preferences
        -> one NVIDIA planning agent using the reviewed bilingual Skill and proposing-state evidence
        -> deterministic scheduling + existing World Model risk/state projection
     <- unsaved itinerary, unbooked stay suggestions, pending checks and planningState
User confirms -> PostgreSQL transaction: trip revision + World Model run/snapshot/events
```

### Enable this mode after adding your private key

Use `server-dotnet/SydneyBuddy.Api/appsettings.MeterlessCloud.example.json` as a configuration reference. The example is not auto-loaded. Set these backend environment variables, or merge the corresponding sections into the environment's private ASP.NET settings:

```text
PlanningAgent__Provider=meterless-cloud
PlanningAgent__AllowCloudModelCalls=true
AllowPaidServices=false
```

Provide `NVIDIA_API_KEY` through the existing backend private `.secrets.json` or service environment; optionally set `NVIDIA_MODEL` through its existing configuration path. Never place keys in App settings, tracked JSON, prompts, command arguments or this document. No key was found during this change, and the user chose to configure it later. No real NVIDIA call is claimed.

The dedicated opt-in permits **only this fixed NVIDIA planning endpoint**. It does not turn on Google or globally set `AllowPaidServices=true`. `assisted` planning can use this mode while the global paid-service flag remains false; `live` Google planning remains blocked. Actual NVIDIA availability, rate limits and billing depend on the account/model. Missing credentials, disabled opt-in, HTTP errors or invalid output produce explicit failures without provider/demo fallback.

`GET /planning/agent/status` reports cloud `configured_not_probed` or `not_configured`; it does not infer successful model connectivity from a saved key. Runtime is `not_selected`. Native clients already consume the provider/configured capability and require no model credentials.

### State ownership and persistence

- `worldState` supplied to the model is backend-produced, request-scoped evidence: proposing phase, known candidate counts, required aliases and confirmed-memory count. Model output cannot overwrite state or constraints.
- Returned `planningState` contains deterministic route-risk projections and remains `awaiting_confirmation`; generating a draft does not create a saved trip or World Model run.
- Confirming a save recomputes state from the accepted route, rather than trusting a client-supplied projection. The trip and its `itinerary_planning` run, snapshot and events commit in **one PostgreSQL transaction**, bound to the accepted trip revision.
- `GET /trips/{tripId}/planning-state` returns the latest planning run. Existing `/trips/{tripId}/world` remains scoped to `travel_simulation`, so the new events do not replace the digital-twin simulation view. Revision changes mark older runs stale.
- H-MEM persistence remains a separate explicit preference-consent flow. Planning reads it; it does not automatically add or rewrite preferences. The shared test backend still uses the existing team identity, not new per-user isolation.
- World Model here means the project's C# application implementation and PostgreSQL append store; no separately hosted official Meterless Runtime is running. Risk projections are rules over evidence, not real-world predictions or verified booking availability.

The Runtime-specific instructions below apply only when selecting `PlanningAgent:Provider=meterless`.

Cloud-mode verification on 2026-10-27: **509 backend tests passed**, including a real PostgreSQL API test with a mock NVIDIA transport, draft/save separation, planning/simulation run separation, stale revision detection, and refusal of Google live mode while global paid services are disabled. **103 iOS ScheduleTests passed**, including state round-trip preservation. Localization resources and 4 English guard tests passed. The cloud model was mocked; no real key, provider request, production deployment or cloud VM restart was performed.

## Data and execution flow

```text
iOS / Android
  -> ASP.NET Core Planner
     -> selected local / local Zetaris / cloud Zetaris POI inventory
        -> activities and dining, separately from accommodation
     -> available daily weather + explicit gaps
     -> consented H-MEM preferences
     -> one Meterless Runtime planning agent (bilingual embedded skill)
     -> validate known IDs, output shape, duplicate visits, dates, must-visits
     -> deterministic route/time/budget/opening-window scheduler
  <- unsaved draft + unbooked accommodation suggestions + outstanding checks
  -> existing user-confirmed save flow
```

The source remains selected by `TravelData:ManagedSource` or `X-Travel-Data-Source`; there is no separate database or copying of POIs for the model. Meterless credentials remain exclusively on the backend. H-MEM is memory, not the model runtime. Selecting Meterless uses the Runtime provider, rather than relabeling an NVIDIA call. There is no silent provider fallback.

## Backend configuration

Use the backend's existing private service environment configuration or standard ASP.NET configuration. The example JSON is a reference, not automatically loaded. To use a JSON copy, merge its sections into a private `appsettings.<ASPNETCORE_ENVIRONMENT>.json` loaded by ASP.NET, never a tracked file. Prefer an existing root-only service environment file in production.

| Configuration | Environment variable | Value |
|---|---|---|
| `PlanningAgent:Provider` | `PlanningAgent__Provider` | `meterless` to select Runtime; default `nvidia` retains existing behavior |
| `Meterless:BaseUrl` | `Meterless__BaseUrl` | Default `http://127.0.0.1:8088/v1`; full `/v1/chat/completions`, `/v1/models`, or `/healthz` URLs also normalize |
| `meterless_runtime_token` | `meterless_runtime_token` | Real Runtime token, copied privately by the operator from Runtime's `proxy-token`; never paste into chat or App |
| `Meterless:Token` | `Meterless__Token` | Alternative token configuration; `meterless_runtime_token` takes precedence |
| `Meterless:Model` | `Meterless__Model` | Default `meterless/auto`; must appear in authenticated `/v1/models` |
| `Meterless:AllowInsecureHttp` | `Meterless__AllowInsecureHttp` | Default false. Explicit opt-in for a trusted HTTP container/tunnel endpoint only; remote deployment should use HTTPS |

`127.0.0.1` refers to the **backend host/container**, not the phone or developer's Mac. In a Docker deployment a separately running host Runtime may require `host.docker.internal` plus an explicit trusted HTTP opt-in, or an authenticated TLS endpoint. An Oracle backend cannot reach a Runtime on a developer Mac through Oracle's loopback address. Do not expose Runtime publicly without authentication/TLS. No new compute resources or paid model routes are enabled by this change; Runtime's own routing/provider billing policy must be configured separately by its operator.

## API contracts

- Existing `/health` adds `planningAgentProvider` and `planningAgentConfigured`. Configured means backend credentials exist, **not** that the Runtime is online. No token or private Runtime URL is returned.
- `GET /planning/agent/status` performs authenticated `GET /v1/models`: `online`, `unauthorized`, `offline`, `not_configured`, or `not_selected`. It never relies on `/healthz` to establish model access. Missing selected mode fails rather than choosing another mode. There is no network autodiscovery in this version; use an explicit backend URL.
- `POST /planning/inventory` accepts a profile with `start: {lat,lng,name}`. It returns bounded activity/dining and lodging lists from the selected source within 15 km. This is local discovery, not a whole-NSW exhaustive planning search. Existing supplied planning candidates remain supported for other areas.
- `/plan/prepare` and `/plan` use the selected Meterless agent even when the legacy route mode is `demo`; route mode describes evidence, not provider selection. Missing/offline Runtime produces a visible failure, never a fake successful itinerary. The Apple two-stage shortlist/road workflow may invoke the **same agent** once per phase; it does not create multiple agents.
- Agent input includes profile, candidate aliases, separate stay aliases, daily weather, source uncertainty, and consented memory. Coordinates/prices/hours remain source facts, never model output.
- Optional request fields: `language` (`en`, `zh-Hant`, `zh-Hans`), `indoorPreference` (`balanced`, `preferIndoor`, `preferOutdoor`), `weatherTolerance` (`standard`, `acceptRain`, `avoidRain`). Native clients send the selected language. The preference fields are API-supported; the existing native preference forms are not expanded in this change.
- Output adds `planningAgent` (provider, draft status, skill version, explanation, outstanding checks) and `accommodationSuggestions`. Native draft views display stays. They do not book or automatically change trip start/end locations; accommodation is excluded from the activity budget. `nightlyRateAud` may be a category-based estimate, not a quote; the stay UI deliberately makes no verified-price claim.

## Planning and weather boundaries

The agent can rank supplied activities and propose per-date preferences. The scheduler treats per-date preferences as soft ranking, while retaining hard feasibility checks and cross-day uniqueness. Client-verified Apple road candidate groups remain authoritative. The final scheduled order may differ from the model's preferences.

Weather preferences only score known settings. An explicit `avoidRain` excludes outdoor/unknown settings on rainy dates; if that conflicts with a must-visit, planning fails visibly. `acceptRain` removes the ordinary rain preference penalty; it never overrides supplied safety restrictions. Meterless plans do not automatically shorten outdoor visit durations just to fit rain preferences.

Current weather is daily data; hourly dry windows, live official warnings, sea/trail conditions, real-time booking availability, accessibility certification, and arbitrary fixed-ticket scheduling are **not newly implemented** by a prompt. The legacy public weather feed is Sydney-scoped; do not assume it proves conditions across NSW. Those gaps are passed to the agent and drafts remain pending verification. The existing legacy `recommendationStatus: ready` means candidate completeness, not a guarantee that all skill constraints were verified; use `planningAgent.status: draft_pending_verification` for that distinction.

The bilingual reviewed policy is adapted into embedded skills under `server-dotnet/SydneyBuddy.Api/Planning/Skills/`. They preserve the planning rules while using a strict machine contract instead of human-readable prose sections. Runtime response is limited to 256 KiB, chat timeout is 180 seconds, redirects are disabled, and provider errors never echo response bodies or tokens.

## Validation and next live step

Automated tests use an in-process mock HTTP handler, not a live Runtime or paid model. They exercise headers, URL normalization, model discovery, authorization/offline distinction, bilingual skill embedding, weather/stay evidence, and rejection of invalid IDs/dates/duplicates. This does **not** claim end-to-end model quality or a successful live Runtime connection.

Validation on 2026-10-27: full backend suite 502 passed; final Meterless-focused rerun 39 passed after the rain-scenario adjustment; iOS build and 103 ScheduleTests passed; Android build and 37 unit tests passed; localization generation check and 4 English guard tests passed. No live Runtime, physical-device, or Oracle deployment test was performed for this branch.

Once Runtime is running, the operator privately configures its URL/token, checks `/planning/agent/status`, then generates an unsaved multi-day trip in the App. Verify weather trade-offs, no duplicate visits, required places, unbooked lodging suggestions, and road validation before confirming a save. Do not merge or deploy until the intended environment has been reviewed.

Official provider reference: https://www.meterless.ai/engines.html (Runtime provider contract, checked 2026-10-27).


## Additive skill and walking load update — 2026-10-27.2

Both embedded Skills retain the previous version verbatim, append the full user-supplied Chinese/English specification verbatim, and add walking-load, hotel-return, unbooked-stay, and energy-feedback rules. No existing instruction was removed. The JSON output contract remains unchanged; all numeric feasibility remains backend-owned.

- New profiles default to 10:00–20:00 in backend, iOS and Android. Existing saved/custom windows and half days remain intact. 20:00 is a soft coverage target inside the hard end time, not permission to exceed fatigue, safety, budgets, bookings, or opening hours. Long days compare bounded route alternatives with up to 4/5/6/7 stops according to energy and pace, and allow separate lunch/dinner windows. The beam retains at most 24 prefixes per depth instead of enumerating all long-day permutations. This is a bounded heuristic, not a proof of a globally optimal or universally feasible itinerary.
- `walkingSpeedKmh` defaults to 4.5 and `stepLengthMeters` to 0.75: derived cadence 100 steps/minute. These are declared engineering assumptions, not measured personal gait or medical recommendations. Route steps use road walking distance / step length. On-site steps use duration × walking fraction × cadence; known indoor defaults to 30%, outdoor 60%, unknown setting 35%, trails/walking tours 100%. Explicit on-site walking evidence takes precedence. Seated meals/rest/check-in are excluded; transit rides, waiting and buffers are not walking. Route providers retain their own duration evidence; the speed setting changes the on-site estimate, not an official timetable.
- `stepEstimate` distinguishes route/on-site/previously completed steps and the hypothetical continuous-walking scenario. `estimatedSteps` remains backward compatible and includes return walking. Planner, schedule editor and Apple walking-time refresh share the same model. Saved stop steps now include their access leg and on-site load, preventing completed progress from losing on-site steps. The hypothetical value assumes every visit minute was spent walking and is never used as the actual daily step cap total.
- Optional `hotel` is a coordinate/name object, with `accommodationStatus=selected|booked` declared by the user. It supplies omitted endpoints only. An explicit airport or other endpoint is preserved, and a non-hotel endpoint is identified as such. Recommendations never choose an origin or hotel automatically. Without hotel selection the endpoint is explicitly provisional; availability/rates/booking confirmation remain unverified. The existing schedule editor continues to support individual nightly stays; this new profile hotel field does not infer hotel changes from free text.
- Optional `energyFeedback=tired|normal|energized` or supported explicit adjustment text changes optional load. Tired feedback reduces the walking allowance to at most 7,000 steps without increasing a lower existing cap. Energetic feedback can increase optional visits, overriding a historical low-energy preference, but never raises the existing hard step cap. Mid-trip edits keep completed/skipped history, required/locked items, and other dates; new stops require supplied access and return road evidence. Feedback is not silently persisted as a long-term memory.
- `targetReturnMinute` defaults to 1200, clipped to the explicit end window. Earlier arrival is allowed and explained. Daily `targetReturnTime`, `returnLocationStatus`, `returnTargetStatus`, and `unfilledMinutes` distinguish near-target, early-for-energy, and constrained incomplete days. Empty time does not become fabricated visits, inflated dwell duration, or verified availability.

Validation: the full backend suite passed 523 tests before the final endpoint/energy refinements; 86 affected planning/editor/Meterless tests passed after those refinements. iOS build and 139 Schedule/PetDraft tests passed; Android debug build and 37 unit tests passed. Localization checks cover 3 languages and the new Android entries. Original and supplied Skill preservation was checked verbatim. This update has not been deployed to Oracle and has not been pushed or merged; live NVIDIA quality and physical-device behavior have not been revalidated for this version.

## Additive route logistics update — 2026-10-27.3

The complete v2 Chinese and English Skill files remain verbatim prefixes of v3. New rules supersede only the user-authorized fixed daily stop counts and old 7,000-step low-energy default. Other requirements remain active.

- Route ranking penalizes walking and backtracking; alternatives containing the same activities prefer less verified walking, including final return. The bounded search is not a global shortest-path guarantee. There is no fixed four-stop product limit. A 30-stop serialization safety bound remains distinct from itinerary targets.
- Missing personal step caps derive from low/normal/high energy as 8,000/10,000/15,000 steps; explicit personal limits remain authoritative. Tired adjustments cap at 8,000 or a lower existing limit. The v2 road/on-site/continuous-scenario calculation and soft 20:00 hotel-return objective remain.
- Booked `hotel` coordinates anchor inventory and default endpoints. Explicit airports/custom endpoints remain intact. iOS can resolve an explicitly booked hotel name using Apple place search, with ambiguity errors rather than guessed branches. Unbooked profiles receive supplied stay recommendations even when the model omits accommodation IDs; suggestions remain unbooked, with no verified vacancy or rate. Multiple hotel dates still need explicit per-date inputs; this does not parse arbitrary reservation documents.
- Optional `heavyLuggage`, `luggageNotes`, `luggageStorage`, `luggageStored`, `luggageStoredDate`, and `luggageDays` carry logistics evidence. Storage points require name and NSW coordinates, with optional confirmed-service flag, hours and cost. Heavy luggage inserts mandatory morning drop-off and final pickup tasks around sightseeing and accounts for roads, handling, cost and steps. Known closed services invalidate the route. A hotel coordinate alone produces a provisional task and incomplete draft, not confirmed storage. Missing coordinates prompt for clarification; free-text area names are not automatically converted into storage facilities. Mid-trip edits preserve completed logistics and stored-bag location.
- Meal slots default to 09:00/12:00/18:00 or optional `mealTimes` (local minutes after midnight). Real meals must fit separate windows; missing restaurant slots are reported rather than fabricated. Lodging recommendations do not become selected endpoints or storage confirmations.

Validation: backend 538 tests passed, including 14 route-logistics regression cases; iOS 141 tests and Android 37 tests passed, with both native builds successful. Both previous Skills were checked as exact prefixes; three-language resources and four English guard checks passed. This revision has not been deployed, pushed or merged, and no new live model, hotel-booking or storage-service verification is claimed.
