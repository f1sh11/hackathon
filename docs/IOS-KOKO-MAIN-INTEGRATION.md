# Koko iOS integration on current main

Baseline: main `28f3002`. Imported the iOS, shared assets and backend memory/dialogue
changes from `codex/travel-dna-koala-20261024` (`5458d10`). Android changes were not
imported. The previous four-scene prototype is preserved on its original local branch.

## Preserved and integrated

- Main's fixed generate CTA, required-data validation, 1–15 day planning, accepted-trip
  workspace navigation, explicit must-go/avoid constraints and revision-safe saves.
- Main's default Apple MapKit in-app walking routes, walking-time calibration, per-day
  edits and undo. Core map/navigation/planner files were retained from main.
- Koko's optional nickname, contextual replies plus free text, maximum four initial
  responses and inspiration stage. Missing destination/date/budget cannot generate a
  real itinerary; inspiration remains clearly provisional.
- Travel DNA evidence, scope, conditions, confirmation/conflict handling, working
  candidates, H-MEM long-term persistence, retrieval and revocation.
- One shared koala player and manifest: ten authored clips, 276 RGBA frames. Static
  message thumbnails; semantic planning/save/consent events; background/reduced-motion
  guards and a bounded decoded-image cache. The four core scenes are wired.

## Integration fixes

- Kept the generate CTA outside chat scrolling and exposed direct adjustment for a
  saved itinerary. Existing-itinerary requirement editing opens its actual editor.
- New trips do not copy legacy habits into explicit trip fields; confirmed DNA can
  supply defaults, and this trip's explicit values win without changing DNA.
- Deterministic fallback retains clear city/date/duration/budget basis and exact steps
  when a conversational model omits fields. Ambiguous alternatives remain unresolved.
- Koko model responses cannot introduce unstated energy, rhythm, queue or walking habits
  into trip fields. Only relevant explicit user evidence permits those updates.
- A plainly stated unconditional numeric daily habit can stage an unconfirmed candidate
  if the model omits it; temporary, conditional and ranged text is not promoted.
- Save feedback has its own layout area and cannot cover the navigation title. A
  routine weather refresh does not replace or interrupt save celebration.
- Collapsed unknown habit dimensions, moved optional consent cards after inspiration,
  and added lightweight listening/thinking indicators with human-readable accessibility.

## Verification (24 October 2026)

- iPhone 18 Pro, iOS 27 simulator: 97 iOS tests passed; final app build succeeded.
- Backend: 246 tests passed, including facts, provenance, consent, conflict and revocation.
- Asset validator: ten actions / 276 numbered transparent 256×256 frames passed.
- Live NVIDIA chat: optional name skip, contextual option + free text, fourth-response
  inspiration, temporary requirements, fixed generate button, accepted save and map.
- Real local memory: confirmed 8000-step habit saved; after backend restart a new
  session recalled it. A trip-only 10000-step override kept the confirmed habit intact.
  Revoking only the QA-created record removed it from active DNA and preserved the trip
  override; all pre-existing memories were retained.
- Two-day route: Opera House included, museum category excluded; Apple walking preview
  stayed in-app. Calibration successfully updated day one while preserving day two.
  Existing itinerary direct adjustment and accepted-save celebration ran.
- The single QA trip is named `Koko整合回归 · 记忆与Apple路线`; pre-existing trips retained.

Routing still uses the configured local demo planner/catalog for itinerary selection;
Apple walking preview uses actual MapKit routes. This is local iOS verification, not
an Android, cloud-deployment or live Zetaris acceptance claim. No credentials or raw
animation archive are included. No remote push is part of this task.
