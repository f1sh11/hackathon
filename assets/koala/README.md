# Sydney Buddy — Koala animation assets

This folder is the target location for the ten **original koala animations** (not generated placeholder art).

## Import

The original ZIP is ~393 MB / 700 files and contains production intermediates. A separately prepared production bundle is **13.9 MB** with 10 actions, 276 numbered 256x256 transparent PNG frames, and ten WebP previews. Five recovery clips add 156 frames after generation.

Place the supplied `Sydney_Buddy_Koala_Assets_Production.zip` in your computer, then from the repository root run:

```bash
python scripts/install_koala_assets.py "/path/to/Sydney_Buddy_Koala_Assets_Production.zip"
python -m pip install Pillow
python scripts/generate_koala_recovery.py --assets assets/koala
python scripts/test_koala_recovery.py --assets assets/koala
git add assets/koala
git commit -m "Add runtime koala animation media"
git push
```

Or extract the ZIP at the repository root, keeping `assets/koala/animations/<action>/frames/001.png`.

**Current branch includes the manifest and integration documentation; media files are NOT uploaded yet.** Do not merge before the PNG/WebP assets have been added and visually checked.

## Playback contract

- Follow `manifest.json`, 12 FPS, 256x256 fixed canvas, transparent PNG numbered frames for native mobile players.
- `idle` loops; `wave`, `clap`, `thumbs-up`, `celebrate` play once and finish in idle.
- `map`, `weather`, `budget`, `tired`, `departure` HOLD at their last frame.
- Recover the five HOLD actions using generated clips: `weather-to-idle`, `map-to-idle`, `budget-to-idle`, `tired-to-idle` and `departure-to-idle`. The last uses a short dissolve and still needs bespoke suitcase-exit artwork.
- Don't restart a hold animation continuously or trigger `celebrate` before a successful save.
- Details: [Koala integration contract](../../docs/KOALA-ANIMATION-INTEGRATION.md).

Note: Android + iOS native apps are the current clients; React Web is not on main.
