# Sydney Buddy koala runtime assets

The approved white koala with coral bag is included here: 10 actions, 276 RGBA PNG frames at 256×256 and 12 FPS, plus reference previews. Media is checked into this branch.

Both native clients package this same directory. Follow `manifest.json`; `framePath` is one-based. Do not infer actions from an LLM mood or restart a held action every few seconds.

See [integration and event contract](../../docs/KOALA-ANIMATION-INTEGRATION.md) and run `python3 scripts/validate_koala_assets.py` from the repository root. The supplied production ZIP can be reinstalled with `scripts/install_koala_assets.py`.

Budget, lying and departure endpoints do not have authored recovery drawings. They remain held instead of snapping/crossfading to Idle.
