# Travel DNA and koala interaction handoff

## Outcome and baseline

Inspected `main` at `39a7399` (24 October 2026, Sydney time). The app already had native iOS/Android, .NET 10, PostgreSQL, planning, maps, weather, saved itineraries and an iOS pet chat. H-MEM was present, but chat preferences were mostly free-text summaries. The animation branch contained a manifest without its media. A complete simulation/optimization skill and Zetaris integration were not present.

This slice implements natural-conversation profiling, candidate/consent/conflict handling and real koala asset playback. No database migration or new application service is required. PostgreSQL continues storing preference JSON; the official vendored H-MEM code is unchanged.

## What removed the questionnaire effect

The free-chat endpoint no longer appends the next fixed question, replaces a no-question reply with a completion script, truncates an ordinary response to generic boilerplate or pouts when a user says they do not know. Missing trip fields remain available to the review panel, rather than controlling every reply. Budget math and supported-city checks remain guarded. The response is buffered until its data is validated; the client receives NDJSON reply/done events, not a claim of raw token streaming.

The first iOS flow opens conversational Buddy directly. Quick-fill controls are opt-in under the menu, using a fresh preference key so old questionnaire defaults do not turn them on. The separate editable personal-preferences form remains available in settings.

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
- `POST /pet/sessions/{id}/profile/complete`: requires at least one confirmed preference and no unresolved candidates. Only successful completion triggers `clap`.
- Existing `PUT/DELETE /memories/{id}` update/revoke memory. Structured iOS edits preserve the DNA key and provenance instead of reducing the record to an untyped summary. Active records are refreshed before subsequent chat/review.

## Animation behavior

See [the exact event table](KOALA-ANIMATION-INTEGRATION.md). All 10 actions and 276 frames are bundled. Ordinary chat is Idle. A preference confirmation is thumbs-up; explicit DNA completion is clap; a successful final trip save is celebrate. Planning and existing schedule recomputation use map. Real available rain information and projected budget overruns have dedicated risk reactions. Simulation/departure/fatigue mappings await the actual Digital Twin engine.

## Verification and review

Local validation: backend compiles, 183 tests pass after integrating main at `0094707`, and official H-MEM conformance passes 13/13. HTTP integration tests ran with a temporary PGlite PostgreSQL socket server in serial mode; the standard repository CI uses native PostgreSQL. Added tests cover a natural zero-question reply, unknown answers, the sunrise exception, trip vs long-term scope, inferred claims, conflict confirmation, stale sessions, updates/revocation and real Node H-MEM candidate/long-term isolation.

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
