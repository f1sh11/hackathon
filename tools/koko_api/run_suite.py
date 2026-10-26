#!/usr/bin/env python3
"""One-command isolated fixture/live host + CLI batch run (requires a dedicated test PostgreSQL connection)."""
from __future__ import annotations

import argparse
import json
import os
import shutil
import signal
import subprocess
import sys
import tempfile
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from koko_api.client import ApiClient, HarnessError
from koko_api.runner import run_batch


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mode", choices=["fixture", "live"], default="fixture")
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--case", action="append", dest="case_ids")
    parser.add_argument("--port", type=int, default=8897)
    parser.add_argument("--language", choices=["zh-Hans", "zh-Hant", "en"], default="zh-Hans")
    parser.add_argument("--no-build", action="store_true")
    args = parser.parse_args(argv)
    root = Path(__file__).resolve().parents[2]
    project = root / "tools/koko_api/FixtureHost/KokoApiFixtureHost.csproj"
    dotnet = os.environ.get("KOKO_DOTNET") or shutil.which("dotnet")
    if not dotnet or not os.environ.get("KOKO_TEST_CONNECTION"):
        parser.error("dotnet 10 and KOKO_TEST_CONNECTION for a dedicated test database are required")
    if not 1024 <= args.port <= 65535:
        parser.error("Use an unprivileged port")
    args.out = args.out.resolve()
    args.out.mkdir(parents=True, exist_ok=True)
    if not args.no_build:
        build = subprocess.run([dotnet, "build", str(project), "-c", "Release", "--nologo"], cwd=root)
        if build.returncode:
            return 2
    dll = project.parent / "bin/Release/net10.0/KokoApiFixtureHost.dll"
    client = ApiClient(f"http://127.0.0.1:{args.port}", timeout=120, language=args.language)
    # Never reuse or shut down a preexisting process at this address.
    try:
        client.request("GET", "/__koko_test__/metadata", timeout=0.3)
    except HarnessError:
        pass
    else:
        print("Refusing an occupied test port", file=sys.stderr)
        return 2
    with tempfile.TemporaryDirectory(prefix="koko-suite-") as temporary:
        environment = dict(os.environ, KOKO_TEST_MODE=args.mode, KOKO_TEST_PORT=str(args.port),
                           KOKO_TEST_DATA=temporary, KOKO_TEST_CLEANUP_ON_EXIT="1", SYDNEYBUDDY_ROOT=str(root))
        if args.mode == "live":
            environment["KOKO_TEST_LIVE_OPT_IN"] = "1"
        # Host output can contain provider diagnostics; keep it temporary, never in a report artifact.
        with tempfile.TemporaryFile(mode="w+") as log:
            host = subprocess.Popen([dotnet, str(dll)], cwd=root, env=environment, stdout=log, stderr=log,
                                    creationflags=subprocess.CREATE_NEW_PROCESS_GROUP if os.name == "nt" else 0)
            try:
                for _ in range(200):
                    if host.poll() is not None:
                        raise HarnessError("Owned test host exited during startup; inspect local environment/connection settings")
                    try:
                        client.verify_environment()
                        break
                    except HarnessError:
                        time.sleep(0.2)
                else:
                    raise HarnessError("Owned test host did not become ready")
                result, paths = run_batch(client, Path(temporary) / "batch-state.json", project.parent.parent / "cases.json",
                                          args.out, mode=args.mode, selected=args.case_ids)
                print(json.dumps({"mode": args.mode, "totals": result["totals"], "reports": paths}, ensure_ascii=False, indent=2))
                return 1 if result["totals"]["FAIL"] else (3 if result["totals"]["NOT_RUN"] else 0)
            finally:
                if host.poll() is None:
                    host.send_signal(signal.CTRL_BREAK_EVENT if os.name == "nt" else signal.SIGINT)
                    try:
                        host.wait(timeout=20)
                    except subprocess.TimeoutExpired:
                        host.kill()
                        host.wait(timeout=5)
    return 2


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except HarnessError as exc:
        print("Harness error:", exc, file=sys.stderr)
        raise SystemExit(2)
