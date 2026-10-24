#!/usr/bin/env python3
"""Check native runtime asset integrity; this does not assess subjective motion quality."""
import json
import struct
from pathlib import Path

root = Path(__file__).resolve().parents[1] / "assets/koala"
manifest = json.loads((root / "manifest.json").read_text(encoding="utf-8"))
expected = {"wave": 12, "idle": 36, "clap": 12, "thumbs-up": 18, "map": 36,
            "departure": 36, "weather": 30, "budget": 36, "tired": 36, "celebrate": 24}
assert {clip["id"] for clip in manifest["animations"]} == set(expected)
count = 0
for clip in manifest["animations"]:
    name = clip["id"]
    assert clip["frames"] == expected[name] and clip["fps"] == 12, name
    assert clip["ending"] == ("loop" if name == "idle" else "idle" if name in {"wave", "clap", "thumbs-up", "celebrate"} else "hold"), name
    if name in {"budget", "tired", "departure"}:
        assert clip["recovery"] == "hold", "Missing authored recovery must hold: " + name
    directory = root / "animations" / name / "frames"
    assert len(list(directory.glob("*.png"))) == clip["frames"], name
    for frame in range(1, clip["frames"] + 1):
        path = root / clip["framePath"].replace("{frame:03}", f"{frame:03}")
        header = path.read_bytes()[:33]
        assert header[:8] == b"\x89PNG\r\n\x1a\n" and header[12:16] == b"IHDR", str(path)
        width, height, depth, colour = struct.unpack(">IIBB", header[16:26])
        assert (width, height, depth, colour) == (256, 256, 8, 6), str(path)
        count += 1
    assert (root / clip["poster"]).is_file() and (root / clip["terminal"]).is_file(), name
assert count == 276
print(f"Koala assets: {len(expected)} actions, {count} RGBA frames; playback endpoints verified.")
