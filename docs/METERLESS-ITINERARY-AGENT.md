# Single Meterless itinerary agent

Branch: `feat/meterless-itinerary-agent`. Runtime is not installed or started by this change. No production/Oracle settings are changed.

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
