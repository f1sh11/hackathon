# Travel DNA and koala interaction handoff

## Outcome and baseline

Inspected `main` at `39a7399` (24 October 2026, Sydney time). The app already had native iOS/Android, .NET 10, PostgreSQL, planning, maps, weather, saved itineraries and an iOS pet chat. H-MEM was present, but chat preferences were mostly free-text summaries. The animation branch contained a manifest without its media. At that original baseline, a complete simulation/optimization skill and Zetaris integration were not present. Latest main at `6f07790` is now integrated into this feature branch, including the Apple Maps flow, Zetaris travel/insight providers and Node 26 H-MEM compatibility repair.

This slice implements natural-conversation profiling, candidate/consent/conflict handling and real koala asset playback. No database migration or new application service is required. PostgreSQL continues storing preference JSON; the official vendored H-MEM logic is retained, with main’s documented Node 26 constructor compatibility patch (see `server/vendor/hmem/UPSTREAM.md`).

## What removed the questionnaire effect

The free-chat endpoint no longer appends the next fixed question, replaces a no-question reply with a completion script, truncates an ordinary response to generic boilerplate or pouts when a user says they do not know. Missing trip fields remain available to the review panel, rather than controlling every reply. Budget math and supported-city checks remain guarded. The response is buffered until its data is validated; the client receives NDJSON reply/done events, not a claim of raw token streaming.

The first iOS flow opens Koko directly. Contextual reply buttons follow the current proposal. The older fixed-field quick-fill controls remain opt-in under the menu. The separate editable personal-preferences form remains available in settings.

## Travel Profiling v2: initial conversation slice

The authoritative product rules are [Travel Profiling v2](TRAVEL-PROFILING-V2-SPEC.md). Section 8 of that document is simulation evidence only; none of its example preferences, nickname, destination or consent is seeded into application data.

iOS draft/onboarding chat opts into `profilingVersion=2`. `KokoDialogue.Enable` handles a brief introduction and optional name, or resumes known context. Explicit nicknames are stored locally for future trips; no account/name inference or long-term memory consent is implied. Existing sessions upgrade through PATCH in place, count their prior user responses and preserve their trip fields/history. Older clients retain their existing contract.

`ProfilingDialogue.responseCount` caps the initial conversation at four successful user-response turns, including the name response. Memory-card decisions and merely focusing the optional name editor do not add turns. An early draft request bypasses the model and immediately returns an inspiration from existing ideas. A fourth response forces the draft even if the model asks another profile question. Later chat can refine it without restarting the interview. Three contextual buttons are available throughout, with free text alongside them. Formal trip generation keeps its existing validated date/budget/group requirements; those requirements do not block inspiration.

The model supplies the connected proposal, evolving hypothetical `ideas`, and contextual `quickReplies`; the server assigns option IDs and enforces completion. A clicked turn includes `questionId` and `optionId` as well as the exact displayed text. Only a current option can be used. The selected question/scene and option are stored with the user message and any extracted preference. Replaying the same completed request returns the saved result; submitting an old option as a new answer fails without advancing the dialogue.

Contextual choices are `source=contextual_choice`, `scope=activity`, `status=trip_only`, with the full question retained as a condition. They cannot fill unrelated trip fields, set global fitness/pace/sleep/medical assumptions or create lasting memories. Conditional choices remain in planning notes even when a broad interest field is already present. Free statements retain the existing evidence/consent gates. V2 model context receives only relevant active H-MEM matches, with at most eight records; a failed recall does not dump every historical record into the conversation.

The inspiration card shows the current ideas, explicit known preferences and dimensions still unknown. Ideas are Koko's proposals, with unverified places marked as such. Profile completion, user consent, successful memory persistence and final itinerary save remain separate events. The initial profile-to-inspiration transition can trigger `clap` even when no lasting memory is saved.

This slice does not claim that every v2 inference boundary is formally verified by a classifier. The extraction prompt states those limits and contextual clicks have additional server guards; live NVIDIA wording and the full product acceptance scenario still need a configured model run. Android's conversational profiling UI remains a later slice. The spec's richer consent/persistence metadata and activity-specific promotion into long-term memory are not new APIs in this slice.

V2 validation adds four-button end-to-end HTTP scenarios, stop-asking shortcuts, stale-option/idempotency checks, contextual provenance and legacy-draft upgrades. After synchronizing main at `6f07790`, all 232 backend tests and 26 Zetaris tool tests pass locally. Native iOS tests additionally cover restored inspiration, unknown formal fields, in-place upgrade and nickname reuse without simulated preference seeding. The iOS workflow also runs on pushes to this draft feature branch, so simulator validation does not depend on merging main.

## Responsibility map and data flow

1. `PetChat.swift` sends a message with a request ID. Message retries reuse that ID.
2. `PetModel.cs` sends recent messages, current trip context, pending claims, active confirmed memories and the `TravelProfiling.SystemPrompt` to the existing NVIDIA channel.
3. `TravelProfiling.cs` validates the extraction against the key schema and original quoted evidence. It separates long-term claims, trip-only requirements and AI inference. The twelve dimensions are supported but are not twelve mandatory questions.
4. `PetEndpoints.cs` saves the conversation, returns Travel DNA + Trip Context + pending preferences and stages candidates through H-MEM. It preserves a graceful local candidate if staging fails and reports that status.
5. `hmem_bridge.ts` uses the official mining hook to store application-validated candidates in a per-session working layer. Their source, confidence, status, evidence and message reference are retained. Query results used for long-term memory contain only confirmed long-term records.
6. Explicit consent calls `PreferenceService`, writing an H-MEM long-term record and a PostgreSQL display/structured record. The existing compensating-save behavior is retained. A failed session save followed by a confirmation retry reuses an equivalent saved preference instead of creating another one.
7. The clients play semantic events. LLM mood is presentation text context, not permission to play a success or risk action.

Read these four sections first: `TravelProfiling.Apply` (evidence and scope), `PetEndpoints.DecidePreference` (consent/conflict/retry), `hmem_bridge.ts` (working vs long-term layers) and `KoalaPlayback.advance` (gesture/hold/recovery policy).

## Claim example

For “我讨厌早起，但为了看日出可以。” the extraction can return two candidates:

```json
[
  {"key":"normal_start_time","value":"late","scope":"long_term","evidence":"我讨厌早起，但为了看日出可以。","confidence":0.95},
  {"key":"early_start_exception","value":"sunrise","scope":"long_term","evidence":"我讨厌早起，但为了看日出可以。","confidence":0.95}
]
```

The server supplies identity, dimension, `source=user_statement`, `status=awaiting_confirmation`, source message ID and conflict metadata. Neither candidate is a confirmed long-term memory until the user accepts it. Conditions have their own field. AI inference is marked, confidence-capped and excluded from planning until confirmed. Companion-specific or explicitly trip-scoped needs do not rewrite the user's lasting habits.

## Contract additions

Session responses retain the old mobile fields and add `travelDna`, `tripContext`, `planningFields`, `pendingPreferences`, `tripPreferences`, `missingImportantFields`, `dnaCompleted` and `memoryWarning`.

`planningFields` derives supported route inputs from confirmed and current explicit preferences. Raw trip context remains separate; an explicit trip value wins over a habitual default. Preference notes are derived at read time, so editing or revoking memory cannot leave an old preference copied into the trip. The review panel and direct-generation path use the same effective fields. Remaining rich preferences are forwarded as contextual notes. The existing planner does not yet enforce every new dimension quantitatively.

- `POST /pet/sessions/{id}/memory`: existing free-text consent remains compatible; structured requests add `candidateId`, `consent`, `dismiss` and `expectedConflicts`.
- Conflicting values for the same key and condition show previous values. The user must confirm against the current memory IDs. A stale confirmation is rejected until the session is refreshed.
- Equivalent saved values are reused. Multiple legacy conflicting records require explicit cleanup through the memory page.
- `POST /pet/sessions/{id}/profile/complete`: requires at least one confirmed preference and no unresolved candidates. Successful completion triggers `clap`; the v2 initial profile-to-inspiration transition also triggers it independently of memory consent.
- Existing `PUT/DELETE /memories/{id}` update/revoke memory. Structured iOS edits preserve the DNA key and provenance instead of reducing the record to an untyped summary. Active records are refreshed before subsequent chat/review.

## Animation behavior

See [the exact event table](KOALA-ANIMATION-INTEGRATION.md). All 10 actions and 276 frames are bundled. Ordinary chat is Idle. A preference confirmation is thumbs-up; explicit DNA completion is clap; a successful final trip save is celebrate. Planning and existing schedule recomputation use map. Real available rain information and projected budget overruns have dedicated risk reactions. Simulation/departure/fatigue mappings await the actual Digital Twin engine.

## Verification and review

Local validation: backend compiles and 232 tests pass after integrating main at `6f07790`; all 26 Zetaris Python tool tests pass. Official H-MEM conformance passed 13/13 before the merge and is rechecked by branch CI against main’s compatibility patch. HTTP integration tests ran with a temporary PGlite PostgreSQL socket server in serial mode; the standard repository CI uses native PostgreSQL. Added tests cover a natural zero-question reply, unknown answers, the sunrise exception, trip vs long-term scope, inferred claims, conflict confirmation, stale sessions, updates/revocation and real Node H-MEM candidate/long-term isolation.

Every runtime PNG has verified dimensions and alpha; Swift syntax parses. Native build and simulator results must be taken from GitHub Actions, not inferred from these local checks. NVIDIA conversational quality still requires a real configured model session; fake-model tests verify the protocol and guards, not the wording a live model will choose.

Suggested manual check: say the sunrise example; confirm both claims; complete DNA; start a five-day Sydney trip with two people and a group-trip activity budget of AUD 2000; change only this trip's walking limit; then correct a lasting habit and review its old value before confirming. Confirm that these trigger wave/Idle/thumbs-up/clap/map/celebrate at the specified moments.

## Debugging map

- Missing extraction: check NVIDIA configuration, model JSON and evidence matching in `TravelProfiling.Apply`.
- Memory not saved: check `memoryWarning`, Node 24 and the H-MEM bridge; inspect the preference service error before claiming persistence.
- Old value still shown: refresh the session and active memory records; do not reuse a stale conflict confirmation.
- Koala missing: check the `koala` folder in the iOS bundle/APK and regenerate the Xcode project after adding sources.
- Wrong reaction: inspect the semantic event emitter, not the LLM mood.
- Pose stuck: check `ending` and `recovery`; held poses without authored recovery are intentional.

A useful small learning change is adding one travel preference key and label, then following its quoted statement through extraction, consent, memory retrieval and the review panel. The important concepts are API contracts, scope/provenance, optimistic conflict checks, compensating writes and a deterministic playback state machine.


## Shared UI colours

The iOS and Android UI use the koala bag's coral family, white fur and warm neutral outlines. Existing `green`/`mint` names remain aliases for the new accent/tint to avoid a broad view refactor.

| Role | Colour |
|---|---|
| Primary buttons/navigation | `#BC3C55` |
| Page background | `#FFF8F5` |
| Soft cards and bubbles | `#FCE4E8` |
| Main text | `#3D3138` |
| Secondary text | `#75666D` |

The sampled bag coral is lighter (`#F86070`); the darker button variant gives white text a computed contrast ratio of 5.36:1. Primary and secondary text against the page background are 11.80:1 and 5.16:1. Definitions live in `SydneyBuddyApp.swift`, `MainActivity.kt` and `IosStyle.kt`.
