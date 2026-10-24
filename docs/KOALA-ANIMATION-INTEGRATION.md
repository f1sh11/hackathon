# Koala animation integration

Current implementation: 2026-10-24. Shared assets live in `assets/koala`; iOS and Android package the same approved white koala with coral bag. Existing artwork is reused; this slice draws no replacement animation.

| Event | Action | Trigger | End |
|---|---|---|---|
| app.welcome | wave | First-time welcome | Idle |
| chat.ready | idle | Waiting, input, ordinary chat | Quiet loop |
| information.submitted | clap | Occasional acknowledgement after information was submitted successfully | Idle |
| step.completed / optimization.progress | thumbs-up | A step completes or optimization reports progress | Idle |
| user.confirmed / conversation.choice | thumbs-up | Saved preference confirmation, accepted change or current option acknowledgement | Idle |
| profile.confirmed | celebrate | The whole initial Travel DNA profile is complete | Idle |
| trip.saved | celebrate | The whole itinerary is successfully saved | Idle |
| journey.started | departure | User confirms the itinerary and starts the journey | Hold suitcase |
| simulation.started | departure | Compatibility event for a real Digital Twin start | Hold suitcase |
| plan.started / optimization.started | map | Planning or optimization is running | Hold map until work ends |
| risk.weather | weather | Available weather data reports a risk | Hold umbrella until released |
| risk.budget | budget | Projected cost exceeds the specified budget | Hold reaction until released |
| risk.energy | tired | Real simulation reports fatigue against known constraints | Hold lying pose until released |
| conversation.rain | weather | Explicit willingness to take an umbrella in the current rain conversation | Existing return sequence, then Idle |
| conversation.long-walk | tired | User explicitly selects 15,000 steps for this trip | Playful reaction, existing return sequence, then Idle |

`PetMood` never selects a celebration or risk animation. The current words/options may trigger the two clearly named **conversation reactions**; those are presentation feedback, not computed weather/fatigue findings or stored fitness claims. A negative rain answer, an unrelated budget of 15,000, or a correction to fewer steps does not trigger them. UUID tokens deduplicate retries. A conversational thumbs-up does not claim that long-term memory was written.

## Playback and existing return assets

The original 10 actions contain 276 transparent 256×256 PNG frames at 12 FPS. Four existing return sequences add 124 frames: weather 26, map 30, budget 32, tired 36. Each first frame is byte-identical to the corresponding action terminal and each last frame is byte-identical to canonical Idle. Their supplied provenance is `source_frame_reversal`; they are complete existing cels played as a return, **not newly drawn recovery bridges**. See [recovery source](../assets/koala/recoveries/SOURCE.md).

- `wave`, `clap`, `thumbs-up`, `celebrate` finish their normal clips in Idle.
- `idle` loops without software body deformation or extra blinking.
- New clients use optional `recoveryId` / `recoveries` metadata. The original `recovery` string remains for older clients, so a parser that ignores new keys remains compatible.
- Map/weather can reverse from the currently reached opening frame if work/conversation changes before reaching their held endpoint. At a complete held endpoint, supplied return clips preserve continuity.
- Real work/risk clips stay held until a new event or Idle request releases them. Conversation rain/long-walk cues carry an explicit `returnsToIdle` flag and recover automatically after the reaction.
- A queued event waits through the current physical return sequence. A normal Idle update cannot discard an accepted gesture. Reduced motion keeps a static image; players pause offscreen/in background.
- The supplied departure return uses a dissolve (`mvp_approximation`), so it is excluded. Departure intentionally remains held until a genuine physical suitcase return is available; no instant reset or crossfade substitutes for it. Closing a screen ends that scene.

## Files and integration

- `ios/SydneyBuddy/KoalaAnimation.swift`: authoritative semantic map, conservative conversation hints, pure playback/return queue, cached image player, scalable `PetAvatar(size:)` and feedback banner.
- `android/.../KoalaAnimation.kt`: matching semantic map, conversation hints, return queue and lifecycle-aware player. `KoalaAvatar(..., returnToIdle=true)` enables temporary conversation reactions.
- `assets/koala/manifest.json` and `recoveries/`: existing original/return cels; action numbering remains unchanged.
- `scripts/validate_koala_assets.py`: counts, RGBA headers and exact start/end seams.
- `scripts/generate_project.py`: includes shared assets in the iOS bundle. Run after adding Swift source/tests.
- `android/app/build.gradle.kts`: includes the same shared asset folder in the APK.
- `PetChat.swift` / entry views emit conversation, information and whole-profile completion events; planning/store views emit real work, risk, trip-save and journey-start events.

The full Digital Twin and optimizer remain separate engines. Typed events are ready for their real outputs; ordinary chat must not manufacture actual risk analysis. The Android event/player contract is shared, while its full conversational profiling screen is separate work.

## Validation

Run `python3 scripts/validate_koala_assets.py`, relevant backend tests and H-MEM conformance. Native policy tests cover token deduplication, explicit event/action meanings, conservative rain/walking interpretation, automatic temporary recovery, retained real risk poses, queued gestures through recovery and missing physical recovery fallback. Cloud native builds are the compiler/test evidence; structural asset checks are not proof of subjective animation quality.
