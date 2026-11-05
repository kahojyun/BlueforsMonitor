#!/usr/bin/env python3
"""Exercise JIT/AOT shipped executables with the same synthetic logs and cache."""
import argparse
from datetime import datetime, timedelta
import json
from pathlib import Path
import shutil
import statistics
import subprocess
import sys
import time
import zipfile
import release


def fixture(logs):
    logs.mkdir(parents=True, exist_ok=True)
    start = datetime(2026, 1, 1)
    for channel, baseline in (("CH6", .012), ("CH5", .8), ("CH1", 45), ("CH2", 4)):
        with (logs / f"{channel} T fixture.log").open("w", encoding="utf-8") as stream:
            for minute in range(14 * 1440):
                date = start + timedelta(minutes=minute)
                spike = .012 if channel == "CH6" and 100 <= minute % 1440 < 110 else 0
                stream.write(f"{date:%d-%m-%y,%H:%M:%S},{baseline + spike:.6f}\n")


def compare(directory, runtime, output, repeats=3, modes=("jit", "aot")):
    directory, output = Path(directory).resolve(), Path(output).resolve()
    output.mkdir(parents=True, exist_ok=True)
    logs = output / "logs"
    fixture(logs)
    executables, sizes = {}, {}
    for mode in modes:
        archive = directory / release.archive_name(runtime, deployment=mode)
        release.validate_archive(archive, runtime, release.version(), mode)
        extracted = output / mode
        if extracted.exists(): shutil.rmtree(extracted)
        if runtime == "osx-arm64":
            subprocess.run(["ditto", "-x", "-k", str(archive), str(extracted)], check=True)
            app = extracted / "Bluefors Monitor.app"
            release.verify_mac_bundle(app)
            executables[mode] = app / "Contents/MacOS/BlueforsMonitor"
        else:
            with zipfile.ZipFile(archive) as zipped: zipped.extractall(extracted)
            executables[mode] = extracted / "BlueforsMonitor/app/BlueforsMonitor.exe"
        sizes[mode] = {"zip_bytes": archive.stat().st_size,
                       "unpacked_bytes": sum(p.stat().st_size for p in extracted.rglob("*") if p.is_file() and not p.is_symlink())}
    runs = []
    # Shared cache tests both directions: JIT writes, AOT reads, then vice versa.
    state = output / "state"
    for iteration in range(-1, repeats + 1):
        if iteration <= 0 and (state / "settings/datasets").exists():
            shutil.rmtree(state / "settings/datasets")
        for mode in (modes if iteration % 2 == 0 else tuple(reversed(modes))):
            state.mkdir(exist_ok=True)
            (state / "report.json").unlink(missing_ok=True)
            (state / "error.txt").unlink(missing_ok=True)
            started = time.perf_counter()
            subprocess.run([str(executables[mode]), "--package-check", str(state), str(logs)], check=True, timeout=120)
            elapsed = (time.perf_counter() - started) * 1000
            report = json.loads((state / "report.json").read_text(encoding="utf-8"))
            if report["dynamic_code"] != (mode == "jit"): raise ValueError("Wrong runtime mode")
            report.update(deployment=mode, iteration=iteration, process_total_ms=elapsed)
            runs.append(report)
            target = output / f"run-{iteration}-{mode}"
            target.mkdir(exist_ok=True)
            for name in ("report.json", "light.png", "dark.png", "event.png", "window.png"):
                shutil.copy2(state / name, target / name)
    summary = {"version": release.version(), "runtime": runtime, "sizes": sizes, "runs": runs,
               "note": "Synthetic 14-day, 80,640-sample fixture; iterations -1/0 each rebuild cache (AOT then JIT). Subsequent runs restart processes with a warm OS/data cache. Not a cold-boot benchmark."}
    summary["medians"] = {mode: {key: statistics.median(r[key] for r in runs if r["deployment"] == mode and r["iteration"] > 0)
        for key in ("window_opened_ms", "first_plot_rendered_ms", "working_set_bytes", "process_total_ms")} for mode in executables}
    release.write_json(output / "comparison.json", summary)
    print(json.dumps(summary["medians"], indent=2))

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--directory", type=Path, required=True)
    parser.add_argument("--runtime", choices=release.RUNTIMES, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--deployment", choices=("jit", "aot"), help="Validate one deployment instead of comparing both")
    args = parser.parse_args()
    compare(args.directory, args.runtime, args.output, modes=(args.deployment,) if args.deployment else ("jit", "aot"))
