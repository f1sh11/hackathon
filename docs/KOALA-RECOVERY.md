# Koala Recovery → Idle (2026-10-24)

## Scope
Five actions originally ended in **hold** and could not be smoothly dismissed:
`weather`, `map`, `budget`, `tired`, `departure`.

This patch generates **play-once recovery clips** from the 256×256, 12 FPS PNG frames already shipped in `assets/koala/animations/*/frames/`.
It keeps the original 10 forward animations unchanged.

## Build

1. Unzip the original Koala runtime asset pack into the repo root so `assets/koala/manifest.json` and `assets/koala/animations/` exist.
2. Install the image library: `python -m pip install Pillow`.
3. Run: `python scripts/generate_koala_recovery.py --assets assets/koala`.
4. Verify: `python scripts/test_koala_recovery.py --assets assets/koala`.
5. Include `assets/koala/recoveries/` and the updated `assets/koala/manifest.json` in the mobile app resources.

The `scripts/install_koala_assets.py` in this branch installs the original asset pack. Running the recovery generator **after installation** is intentional; generated recovery assets are not yet pushed in binary form to this branch.

## Semantic state machine

```
IDLE --on event--> PLAYING(action) --after n frames--> HOLDING(action)
  ^                                                 |
  |---- RECOVERING(action-to-idle) <--on dismiss------|
```

- Do not loop `recoveries/*`; they all end in exactly the same pixels as `idle` frame 001.
- Do not start recovery after *every LLM token*; only after a semantic state change (e.g. `optimization.completed`).
- New high-priority events may cancel a held state and queue a recovery before switching to another action.
- De-duplicate triggers with `operationId`; stale network responses must not override newer animations.
- `weather` should only run when a weather hazard was actually verified (not when weather is unknown); same for `budget` and `tired`.
- For motion accessibility settings, show the idle or current poster frame instead.

## Recovery design

| Held action | New recovery | Duration | Source-frame method | QA notes |
|---|---|---:|---|---|
| `weather` | `weather-to-idle` | 2.17s | reverse umbrella opening | Reversible wind motion may look unnatural; inspect |
| `map` | `map-to-idle` | 2.50s | reverse map unfolding | The map folds and hand returns |
| `budget` | `budget-to-idle` | 2.67s | reverse money check and slump | Note the money moves backwards along source path |
| `tired` | `tired-to-idle` | 3.00s | reverse lie-down | Sit up and return to standing; avoid foot teleport |
| `departure` | `departure-to-idle` | 2.67s | reverse suitcase handling + short idle dissolve | **Approximation:** needs bespoke suitcase set-down/exit cels for production |

The user explicitly requested natural full-body motion and accurate foot placement. Reversal preserves the registration of each existing source frame but does **not** magically solve an original source drawing that shifts the feet. View at 12 FPS on device and revise artists' cels before treating the result as approved.

## Integration protocol

```json
{
  "kind": "simulation.completed",
  "operationId": "op-123",
  "koala": {"finishAction": "departure", "next": "idle"}
}
```

The frontend reads `assets/koala/manifest.json`, finds `animations[].recoveryId`, and plays `recoveries[].framePath` at `recoveries[].fps` before looping `idle`.

**Important:** Existing Android/iOS clients do not yet play these frame folders. They must load them as bundled assets and connect the semantic events to a native frame player. Do not claim the app is wired up before separate Android and iOS testing.

## Automated checks

- 5 recoveries exist, contain correct counts, same 256×256 RGBA canvas, and 12 FPS metadata.
- Frame 001 is byte-identical to held action's final frame.
- Recovery's final frame is byte-identical to `idle/frames/001.png`.
- `manifest.json` links all 5 held actions to their recovery IDs.
- `wave`, `clap`, `thumbs-up`, `celebrate` already return to idle; `idle` loops. No extra recovery required.