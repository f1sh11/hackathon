#!/usr/bin/env python3
"""Safely install the curated Sydney Buddy koala runtime bundle into this repository."""
from pathlib import Path
from zipfile import ZipFile
import json
import sys

repo = Path(__file__).resolve().parents[1]
if len(sys.argv) != 2:
    raise SystemExit("Usage: python scripts/install_koala_assets.py /path/to/Sydney_Buddy_Koala_Assets_Production.zip")
zip_path=Path(sys.argv[1]).expanduser()
if not zip_path.is_file():
    raise SystemExit(f"Bundle not found: {zip_path}")
count=0
with ZipFile(zip_path) as z:
    for info in z.infolist():
        if info.is_dir() or not info.filename.startswith("assets/koala/"):continue
        parts=Path(info.filename).parts
        if ".." in parts or info.filename.startswith("/"):raise ValueError("Unsafe archive path")
        target=(repo / info.filename).resolve()
        if not target.is_relative_to(repo):raise ValueError("Archive path escapes repository")
        target.parent.mkdir(parents=True,exist_ok=True)
        with z.open(info) as source, target.open("wb") as dest:
            import shutil
            shutil.copyfileobj(source,dest)
        count+=1
manifest=json.loads((repo/"assets/koala/manifest.json").read_text(encoding="utf-8"))
if len(manifest["animations"])!=10:raise RuntimeError("Expected ten actions")
for item in manifest["animations"]:
    frames=list((repo/"assets/koala/animations"/item["id"]/"frames").glob("*.png"))
    if len(frames)!=item["frames"]:
        raise RuntimeError(f"Wrong frame count for {item['id']}: {len(frames)}")
print(f"Installed and validated {count} files, 10 actions. Run git add assets/koala and push.")
