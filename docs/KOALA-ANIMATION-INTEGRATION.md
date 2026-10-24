# Sydney Buddy — Koala animation / Agent event contract

Based on remote `main` and the user-provided koala animation pack (2026-10-24).

## Core design

**One Travel Agent, three Skills; Koala is the presentation layer, not a fourth Agent.**

```text
User ↔ Android/iOS Koala UI
       ↓
Travel Profiling Skill → Meterless H-MEM (confirmed preferences)
       ↓
Itinerary Planning Skill → existing Planner / JourneyService + proposed Zetaris provider
       ↓
Travel Simulation & Optimization Skill → rule engine and before/after report
       ↓
Koala plays a semantic event mapped to an action, then returns/holds as configured
```

The backend returns structured data and semantic events. The LLM must **not** emit asset paths.

## Event → action mapping

| Trigger | Animation | When |
|---|---|---|
| `app.welcome` | `wave` | App welcome/onboarding; once |
| `chat.ready` | `idle` | Listening, typing, ordinary chat; gentle loop |
| `profile.confirmed` | `clap` | User confirmed Travel DNA |
| `user.confirmed` | `thumbs-up` | A preference or proposed modification is actually accepted and saved |
| `plan.started`, `optimization.started` | `map` | Started planning; hold on terminal map-thinking frame while waiting |
| `simulation.started` | `departure` | Digital twin begins travel simulation; hold suitcase pose |
| `risk.weather` | `weather` | Verified high weather risk, not when data is missing |
| `risk.budget` | `budget` | Cost projection exceeds known user budget |
| `risk.energy` | `tired` | Model identifies high fatigue risk based on stated thresholds |
| `trip.saved` | `celebrate` | After API/DB save succeeded; not on optimistic UI action |

## Animation state machine

```text
Idle -> Playing(action) -> Holding(action) -> Recovering -> Idle
                         \-> Completed -> Idle
```

- `idle`: loop 36 frames / 3s.
- `wave`, `clap`, `thumbs-up`, `celebrate`: play once, return to idle.
- `map` and `weather`: play once, HOLD. On completion/dismissal play frames in reverse as temporary return animations.
- `tired`, `budget`, `departure`: play once, HOLD. On dismissal crossfade 200–250 ms until purpose-made recovery animation is produced.
- In particular, **do not snap a koala lying down into a standing idle pose.**
- Cap repeated triggers (e.g. one celebration per successful save) and select one leading risk animation even if several risks are present.
- Abort stale operations using `operationId` and reject late animation events.
- If reduced motion is enabled, show `poster.png` and clear accessible text.

## Suggested native integration

- Android: new `KoalaAnimationController.kt` (event + state machine), `KoalaAnimationView.kt` (frame player); wire through existing `BuddyStore.kt`, `MainActivity.kt`, `IosStyle.kt`.
- iOS: new `KoalaAnimationState.swift`, `KoalaAnimationView.swift`; wire through `SydneyBuddyApp.swift`, `Models.swift`.
- API: return events/risks from dedicated planning, simulation and conversation endpoints. Keep paths and FPS in client manifest, not ASP.NET `Planner.cs`.
- Keep 256x256 fixed display box, `ContentScale.Fit` / `.scaledToFit()`, and consistent bottom anchor across action changes.
- Do not build a React interface until the team explicitly chooses to restore a Web client.

## Integrations & missing work

- **Meterless H-MEM** is already in `server/vendor/hmem/` via `server/hmem_bridge.ts`. Expand confirmed Travel DNA (morning/queue/energy/weather/transit), consent and multi-session conflict handling.
- **Zetaris** is not integrated in current main. Add a provider and provenance-aware normalized inputs. Do not say a demo estimate is live Zetaris data.
- Existing `Planner.cs`, `RouteScheduler.cs` and `ScheduleEditorService.cs` cover some constraints, but a full four-dimensional simulation and before/after optimizer are still needed.
- Koala conversation feature on Issue #8 is not merged into main; test 409 profile conflicts and restore flows.

## Acceptance checks

1. Welcome wave once, idle loop unobtrusive, wave/clap align with idle size and feet.
2. Planning map plays once and holds while request is pending (no 3s restart).
3. Weather/budget/fatigue risks each select correct one-shot HOLD animation with real evidence.
4. Dismiss fatigue: brief smooth recovery, never abrupt pose jump.
5. Save returns 409: do not play thumbs-up or celebration.
6. Save succeeds: celebrate once and return idle.
7. Navigation or a newer request cancels stale action events.
8. On Android and iOS, test all ten actions, opacity/alpha, no cropping, frame timing, reduce-motion setting.

## Art QA observations

- Ten supplied animations: `wave` 12, `clap` 12, `weather` 30, `budget` 36, `tired` 36, `map` 36, `celebrate` 24, `departure` 36, `thumbs-up` 18, `idle` 36.
- Original first frame dimensions vary: wave/clap 512x512; most others 768x768. Production importer normalizes wave/clap once per action, not per frame.
- Original five HOLD actions do not contain authored return clips. Reverse for map/weather is a **temporary approximation**; standing up, budget recovery and departure cleanup still need artwork.
- The production ZIP has 276 PNG frames; WebP previews can merge identical adjacent frames. Use PNGs for deterministic 12 FPS playback.
