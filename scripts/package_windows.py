#!/usr/bin/env python3
"""Package a published win-x64 directory with a native, console-free root launcher.
Requires Python 3 and either Zig or cl.exe in a VS x64 developer shell.
"""
import argparse
import json
import os
import re
from pathlib import Path
import shutil
import subprocess
import tempfile
import zipfile

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument("publish", type=Path)
parser.add_argument("output", type=Path)
parser.add_argument("--build-info", required=True, type=Path)
args = parser.parse_args()
publish, output = args.publish.resolve(), args.output.resolve()
if not (publish / "BlueforsMonitor.exe").is_file():
    raise SystemExit("Expected a self-contained win-x64 publish directory")
output.parent.mkdir(parents=True, exist_ok=True)
with tempfile.TemporaryDirectory(prefix="bluefors-package-") as scratch:
    stage = Path(scratch) / "BlueforsMonitor"
    stage.mkdir()
    shutil.copytree(publish, stage / "app")
    for name in ("LICENSE", "THIRD_PARTY_NOTICES.md"):
        shutil.copy2(root / name, stage / name)
    shutil.copytree(root / "licenses", stage / "licenses")
    shutil.copy2(args.build_info, stage / "BUILD-INFO.json")
    version = json.loads(args.build_info.read_text(encoding="utf-8"))["version"]
    exe = stage / "BlueforsMonitor.exe"
    source = root / "packaging/windows-launcher.c"
    if shutil.which("zig"):
        details = subprocess.check_output(["zig", "env"], text=True)
        try:
            lib_dir = json.loads(details)["lib_dir"]
        except json.JSONDecodeError:  # New Zig versions emit ZON instead of JSON.
            lib_dir = json.loads(re.search(r'\.lib_dir\s*=\s*("[^"\n]+")', details).group(1))
        include = Path(lib_dir) / "libc/include/any-windows-any"
        subprocess.run(["zig", "cc", "-target", "x86_64-windows-gnu", "-isystem", str(include), "-Os", "-ffreestanding", "-fno-stack-protector", "-nostdlib", "-Wl,--subsystem,windows", "-Wl,--entry,Start", str(source), "-lkernel32", "-luser32", "-o", str(exe)], check=True, cwd=scratch)
    elif os.name == "nt" and shutil.which("cl"):
        subprocess.run(["cl", "/nologo", "/utf-8", "/Os", "/GS-", str(source), "/Fe:" + str(exe), "/link", "/NODEFAULTLIB", "/ENTRY:Start", "/SUBSYSTEM:WINDOWS", "kernel32.lib", "user32.lib"], check=True, cwd=scratch)
    else:
        raise SystemExit("Install Zig, or run in a VS x64 developer shell with cl.exe available")
    (stage / "使用说明.txt").write_text(f"""Bluefors Monitor {version} · Windows x64
完整解压 ZIP 后，双击本目录唯一的 BlueforsMonitor.exe。
app 文件夹包含程序和 .NET 运行时，请完整保留，无需另外安装 .NET。
默认主题跟随系统，日志时间 UTC+08:00。
R / 图内双击：重排；A / 纵轴双击：适配选中轴。
自动轴只调整未手动缩放的通道，R 可恢复全部通道为自动。
需要释放索引空闲空间时可使用“更多 → 压缩本机索引”。
本版使用全新 BlueforsMonitor 设置目录；首次启动请选择 Logfiles 重新索引。
不包含实验日志或预置数据库。源码：https://github.com/kahojyun/BlueforsMonitor
""", encoding="utf-8-sig")
    with zipfile.ZipFile(output, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for path in sorted(stage.rglob("*")):
            if path.is_file():
                archive.write(path, "BlueforsMonitor/" + path.relative_to(stage).as_posix())
with zipfile.ZipFile(output) as archive:
    if archive.testzip() is not None:
        raise SystemExit("Archive integrity check failed")
print(output)
