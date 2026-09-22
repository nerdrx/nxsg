#!/usr/bin/env python3
"""Run the pinned Linux fixture; graphics modes stay inside headless Gamescope."""
import argparse
import json
import math
import os
from pathlib import Path
import shutil
import subprocess
import sys
import signal

ROOT = Path(__file__).resolve().parents[1]
VERSION = "2022.3.22f1"


def run_process(command, *, env, cwd=None, stdout=None, timeout=None, new_session=True):
    """Run command and reap its whole process group on timeout or interrupt."""
    process = subprocess.Popen(command, env=env, cwd=cwd, stdout=stdout,
                               stderr=subprocess.STDOUT if stdout is not None else None,
                               start_new_session=new_session)

    def stop_group(force=False):
        try:
            if new_session:
                os.killpg(process.pid, signal.SIGKILL if force else signal.SIGTERM)
            elif force:
                process.kill()
            else:
                process.terminate()
        except ProcessLookupError:
            pass

    try:
        process.wait(timeout=timeout)
    except subprocess.TimeoutExpired:
        stop_group()
        try:
            process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            stop_group(force=True)
            process.wait()
        stop_group(force=True)
        return 124
    except KeyboardInterrupt:
        stop_group()
        try:
            process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            stop_group(force=True)
            process.wait()
        stop_group(force=True)
        raise
    return process.returncode


def positive_seconds(value):
    seconds = float(value)
    if not math.isfinite(seconds) or seconds <= 0:
        raise argparse.ArgumentTypeError("must be positive")
    return seconds


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=["compile", "smoke", "editor"])
    parser.add_argument("--inside", action="store_true", help=argparse.SUPPRESS)
    parser.add_argument("--timeout", type=positive_seconds, default=600,
                        help="automated compile/smoke timeout in seconds (default: 600)")
    args = parser.parse_args()
    work = ROOT / "work" / "unity"
    work.mkdir(parents=True, exist_ok=True)
    editor = os.environ.get("UNITY_EDITOR")
    if not editor:
        hub = Path.home() / ".config/unityhub/editors-v2.json"
        if hub.exists():
            for entry in json.loads(hub.read_text()).get("data", []):
                if entry.get("version") == VERSION:
                    editor = next((p for p in entry.get("location", []) if Path(p).is_file()), None)
                    if editor:
                        break
    if not editor or not Path(editor).is_file():
        parser.error("Set UNITY_EDITOR to the Unity 2022.3.22f1 executable.")
    env = os.environ.copy()
    libraries = env.get("NXSG_UNITY_LIBS")
    if libraries:
        env["LD_LIBRARY_PATH"] = libraries + (":" + env["LD_LIBRARY_PATH"] if env.get("LD_LIBRARY_PATH") else "")
    version = subprocess.run([editor, "-version"], capture_output=True, text=True, env=env, timeout=30)
    if version.returncode or VERSION not in version.stdout:
        parser.error("Pinned Editor failed its version check: " + (version.stderr or version.stdout).strip())
    if args.mode != "compile" and not args.inside:
        if not shutil.which("gamescope"):
            parser.error("Graphics checks require Gamescope; no visible desktop fallback is used.")
        command = ["gamescope", "--backend", "headless", "-W", "1440", "-H", "900", "-w", "1440", "-h", "900", "-r", "30", "--",
                   sys.executable, str(Path(__file__).resolve()), args.mode, "--inside", "--timeout", str(args.timeout)]
        result_path = work / (args.mode + "-exit.json")
        result_path.unlink(missing_ok=True)
        if args.mode == "editor":
            (work / "close-editor").unlink(missing_ok=True)
        with (work / (args.mode + "-compositor.log")).open("w") as log:
            compositor_status = run_process(command, env=env, stdout=log, timeout=None if args.mode == "editor" else args.timeout + 10)
        # Gamescope can exit successfully even when its primary child failed.
        return json.loads(result_path.read_text())["exitCode"] if result_path.exists() else (compositor_status or 1)
    if args.inside:
        (work / "nested-display.json").write_text(json.dumps({"DISPLAY": env.get("DISPLAY"), "WAYLAND_DISPLAY": env.get("WAYLAND_DISPLAY")}, indent=2))
    command = [editor, "-projectPath", str(ROOT / "DevProject"), "-job-worker-count", "4", "-logFile", str(work / (args.mode + ".log"))]
    if args.mode == "compile":
        command += ["-batchmode", "-nographics", "-quit"]
    elif args.mode == "smoke":
        command += ["-batchmode", "-force-glcore", "-executeMethod", "NxsgSmoke.Run"]
    else:
        command += ["-force-glcore", "-executeMethod", "NxsgSmoke.OpenEditor"]
    status = run_process(command, env=env, cwd=ROOT,
                         timeout=None if args.mode == "editor" else args.timeout,
                         new_session=not args.inside)
    if args.inside:
        (work / (args.mode + "-exit.json")).write_text(json.dumps({"exitCode": status}))
    return status


if __name__ == "__main__":
    sys.exit(main())
