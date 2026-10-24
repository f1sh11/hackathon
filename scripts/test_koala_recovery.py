#!/usr/bin/env python3
"""Check hold-to-idle clips have no first/last frame snaps."""
import argparse,json
from pathlib import Path
from PIL import Image
EXPECTED={"weather":26,"map":30,"budget":32,"tired":36,"departure":32}
def check(root):
    manifest=json.loads((root/"manifest.json").read_text(encoding="utf-8"))
    outputs={e["sourceAction"]:e for e in manifest["recoveries"]}
    actions={e["id"]:e for e in manifest["animations"]}
    with Image.open(root/"animations/idle/frames/001.png") as f:
        idle=f.convert("RGBA").tobytes()
    for name,count in EXPECTED.items():
        info=outputs[name]
        assert info["fps"]==12 and info["frames"]==count
        assert actions[name]["recoveryId"]==info["id"]
        frames=sorted((root/"recoveries"/info["id"]/"frames").glob("[0-9][0-9][0-9].png"))
        assert len(frames)==count,(name,len(frames),count)
        for path in frames:
            with Image.open(path) as img:
                assert img.size==(256,256) and img.mode=="RGBA",path
        forward=sorted((root/"animations"/name/"frames").glob("[0-9][0-9][0-9].png"))
        with Image.open(forward[-1]) as last, Image.open(frames[0]) as first:
            assert last.tobytes()==first.tobytes(),name+" start mismatch"
        with Image.open(frames[-1]) as last:
            assert last.tobytes()==idle,name+" idle mismatch"
        print("PASS",name,count,"frames")
    print("PASS 156 recovery frames")
if __name__=="__main__":
    parser=argparse.ArgumentParser()
    parser.add_argument("--assets",type=Path,default=Path("assets/koala"))
    check(parser.parse_args().assets)
