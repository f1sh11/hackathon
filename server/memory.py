"""Calls the unchanged Meterless H-MEM reference engine through Node 24."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import threading
from planner import ServiceError
ROOT=Path(__file__).resolve().parent
LOCK=threading.Lock()
def run(action, **kwargs):
    node=os.environ.get('NODE_BINARY') or shutil.which('node')
    bundled=Path.home()/'.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node'
    if not node and bundled.exists():node=str(bundled)
    if not node:raise ServiceError('H-MEM requires Node.js 24. Install it and try again.')
    directory=ROOT/'.hmem'
    directory.mkdir(mode=0o700,exist_ok=True)
    payload={'action':action,'directory':str(directory),**kwargs}
    with LOCK:
        result=subprocess.run([node,'--experimental-transform-types',str(ROOT/'hmem_bridge.ts')],input=json.dumps(payload),text=True,encoding='utf-8',capture_output=True,timeout=15)
    if result.returncode:
        raise ServiceError('The H-MEM operation failed. Memory storage or withdrawal has not been confirmed.')
    return json.loads(result.stdout)
