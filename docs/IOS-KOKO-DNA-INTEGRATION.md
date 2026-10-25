# Koko interaction integration on main

Base: main `3767662`. Selected source: `codex/travel-dna-koala-20261024` (`a429375`). This is a selective iOS integration, not a replacement of main's app/store/backend.

- Imported authored map/weather/budget/tired recovery clips and playback queue tests. Original 276 animation frames are unchanged. Recovery provenance is in `assets/koala/recoveries/SOURCE.md`.
- Reused the coastal background and visible-body sizing, plus raised option buttons. Kept main's berry palette and existing three tabs.
- First-time personal conversations use their own `personalProfile` session with bounded profiling and no trip defaults. Finishing/skipping records only an introduction marker; consent-based long-term memory storage remains separate. Returning users are not forced through another introduction.
- New trip conversations show speech above Koko. Options stay above the text composer; history remains expandable. Keyboard mode uses compact speech and avatar, preserving both input methods and unsent text.
- Retained main's real required-field validation, direct generate action, background progress, no automatic navigation, grouped detail edits, Apple routes and World Model checks.
- Completion celebrates on the saved trip card/home companion. No blocking completion page. Only an active non-simulated navigation session emits departure; simulation uses the map gesture.

No server or Android business logic is replaced. Existing saved trips, memory consent, normalized budget semantics and transport preferences remain on main's implementation.
