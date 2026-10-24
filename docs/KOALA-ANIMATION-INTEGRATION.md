# Koala animation integration

Current implementation: 2026-10-24. Shared assets live in `assets/koala`; iOS and Android package the same folder. The approved white koala with coral bag replaces the old drawn mascot.

| Event | Action | Trigger | End |
|---|---|---|---|
| app.welcome | wave | First-time welcome | Idle |
| chat.ready | idle | Thinking about an answer, input, ordinary chat | Quiet loop |
| profile.confirmed | clap | User completes confirmed Travel DNA | Idle |
| user.confirmed | thumbs-up | Preference saved or an accepted change | Idle |
| plan.started / optimization.started | map | Planning or optimization runs | Hold map |
| simulation.started | departure | Digital Twin starts | Hold suitcase |
| risk.weather | weather | Available weather result reports rain risk | Hold umbrella |
| risk.budget | budget | Projected cost exceeds the specified budget | Hold reaction |
| risk.energy | tired | Simulation reports high fatigue against a known threshold | Hold lying pose |
| trip.saved | celebrate | Successful final itinerary save | Idle |

`PetMood` does not select an animation. LLM excitement cannot trigger a clap or celebration. Existing messages use static artwork. Welcome is remembered on the device; a resumed chat does not welcome again. A UUID event token guards repeated events.

## Playback

10 actions, 276 transparent 256×256 PNG frames, 12 FPS. The numbered PNGs are authoritative. WebP previews are reference media.

- `wave`, `clap`, `thumbs-up`, `celebrate` finish in Idle.
- `idle` loops without extra software blinking, floating or body deformation.
- `map` and `weather` hold. If the same player is asked to leave them, it reverses existing frames from the current pose. This is a temporary reversible playback, not a newly authored recovery.
- `budget`, `tired`, `departure` hold. There is no authored return bridge, so the player does not crossfade or snap to standing. Closing a feedback view ends that scene.
- A new event coalesces behind the current gesture. A normal-chat Idle update does not discard an accepted confirmation event.
- Players stop updating off screen/in the background. Reduced motion keeps a static image.

## Files

- `ios/SydneyBuddy/KoalaAnimation.swift`: semantic event map, pure playback state, cached native image player and feedback banner.
- `android/.../KoalaAnimation.kt`: matching event map, playback state and lifecycle-aware Compose player.
- `scripts/generate_project.py`: includes the shared folder as an iOS bundle resource. Run after adding Swift source or tests.
- `android/app/build.gradle.kts`: adds shared assets to the APK without copying them into another source folder.
- `ios/SydneyBuddy/PetChat.swift`: welcome, individual confirmation and explicit DNA completion.
- `Models.swift`, `SydneyBuddyApp.swift`, `BuddyStore.kt`, `MainActivity.kt`: planning, known weather/budget results and post-save feedback.

## Remaining dependencies

The repository does not yet implement a complete Digital Twin or four-dimensional optimizer. `simulation.started`, `risk.energy` and `optimization.started` have typed mappings; the future engines must emit them from real results. Do not manufacture events from words such as “tired” in chat. The schedule editor can display the map animation while it recomputes its existing rules.

Android still uses its existing option-based onboarding and existing AI screen; this change does not create an Android streaming Travel DNA chat screen. Full conversational profiling is implemented in the shared API and iOS flow.

## Verification

Run `python3 scripts/validate_koala_assets.py`, backend tests and official H-MEM conformance. Native playback policy tests cover event deduplication, return to Idle, reverse-map recovery and held lying endpoints. The iOS GitHub workflow builds and tests an installed iPhone simulator; its result is the native build evidence. Structural checks and playback policy tests do not prove subjective animation quality.
