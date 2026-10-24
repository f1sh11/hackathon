#!/usr/bin/env python3
"""Generate 5 hold-to-idle clips from original 256x256/12fps Koala frames.

Run after installing the animation pack:
    python -m pip install Pillow
    python scripts/generate_koala_recovery.py --assets assets/koala

The source PNGs stay untouched. 'departure' uses a short dissolve,
a documented temporary substitute for a hand-drawn suitcase exit.
"""
import argparse
import json
from pathlib import Path
from PIL import Image

FPS=12
DIM=(256,256)
CLIPS={
    "weather":(26,"close_umbrella_reverse",0),
    "map":(30,"fold_map_reverse",0),
    "budget":(32,"put_money_away_reverse",0),
    "tired":(36,"sit_up_and_stand_reverse",0),
    "departure":(32,"return_suitcase_and_dissolve",7),
}

def smoothstep(x):
    return x*x*(3-2*x)

def frames(root):
    seq=sorted((root/"frames").glob("[0-9][0-9][0-9].png"))
    if not seq: raise FileNotFoundError(f"No PNG frames at {root}")
    out=[]
    for file in seq:
        with Image.open(file) as im: image=im.convert("RGBA")
        if image.size!=DIM: raise ValueError(f"Wrong size {file}")
        out.append(image)
    return out

def build(root):
    data=json.loads((root/"manifest.json").read_text(encoding="utf-8"))
    idle=frames(root/"animations"/"idle")[0]
    entries=[]
    for action,(count,method,blend) in CLIPS.items():
        source=frames(root/"animations"/action)
        reverse_count=count-blend
        result=[]
        for idx in range(reverse_count):
            t=idx/(reverse_count-1)
            pos=round((len(source)-1)*(1-smoothstep(t)))
            result.append(source[pos].copy())
        if blend:
            base=result[-1]
            result += [Image.blend(base,idle,smoothstep(i/blend)) for i in range(1,blend+1)]
        else:
            result[-1]=idle.copy()
        assert result[0].tobytes()==source[-1].tobytes(),action+" beginning"
        assert result[-1].tobytes()==idle.tobytes(),action+" ending"
        out=root/"recoveries"/(action+"-to-idle")
        directory=out/"frames"
        directory.mkdir(parents=True,exist_ok=True)
        for existing in directory.glob("[0-9][0-9][0-9].png"):
            existing.unlink()
        for i,im in enumerate(result,1):
            im.save(directory/f"{i:03d}.png",optimize=True)
        result[0].save(out/"poster.png",optimize=True)
        result[-1].save(out/"terminal.png",optimize=True)
        result[0].save(out/"preview.webp",save_all=True,append_images=result[1:],
                       duration=round(1000/FPS),loop=1,quality=77,method=4)
        record={
            "id":action+"-to-idle","sourceAction":action,
            "purpose":"recover_to_idle","method":method,
            "fps":FPS,"frames":count,"durationMs":round(count*1000/FPS),
            "canvas":[256,256],"playback":"once","ending":"idle",
            "framePath":f"recoveries/{action}-to-idle/frames/{{frame:03}}.png",
            "poster":f"recoveries/{action}-to-idle/poster.png",
            "preview":f"recoveries/{action}-to-idle/preview.webp",
            "terminal":f"recoveries/{action}-to-idle/terminal.png",
            "quality":"mvp_approximation" if action=="departure" else "source_frame_reversal"
        }
        entries.append(record)
        print(f"{record['id']}: {count} frames / {record['durationMs']}ms")
    linked={e["sourceAction"]:e["id"] for e in entries}
    for clip in data["animations"]:
        if clip["id"] in linked:
            clip["recoveryId"]=linked[clip["id"]]
            clip["autoRecover"]=False
            clip["recovery"]="play_recovery_clip"
        elif clip["ending"]=="idle":
            clip["recoveryId"]=None
    data["recoveries"]=entries
    data["recoveryPolicy"]={
        "afterHold":"play recovery when semantic event clears held state",
        "interrupt":"cancel stale event, optionally recover before next action",
        "maxConsecutiveWarnings":1,
        "reducedMotion":"show poster, skip playback",
        "note":"Departure is an approximation, not a bespoke suitcase-exit animation."
    }
    (root/"manifest.json").write_text(json.dumps(data,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")

if __name__=="__main__":
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument("--assets",type=Path,default=Path("assets/koala"))
    build(p.parse_args().assets.resolve())
