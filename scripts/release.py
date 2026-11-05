#!/usr/bin/env python3
"""Versioned local packaging and draft-only GitHub releases. Uses standard-library Python."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import plistlib
import re
import shutil
import struct
import subprocess
import sys
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parents[1]
RUNTIMES = ("win-x64", "osx-arm64")


def version(root=ROOT):
    value = (root / "VERSION").read_text(encoding="utf-8").strip()
    if not re.fullmatch(r"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)", value):
        raise ValueError("VERSION must contain a numeric X.Y.Z version")
    return value


def command(args, **kwargs):
    return subprocess.check_output(args, cwd=ROOT, text=True, **kwargs).strip()


def run(args):
    subprocess.run(args, cwd=ROOT, check=True)


def sha256(path):
    digest = hashlib.sha256()
    with Path(path).open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def archive_name(runtime, value=None):
    return f"BlueforsMonitor-{value or version()}-{runtime}.zip"


def write_json(path, value):
    Path(path).write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def checksums(directory, names):
    directory = Path(directory)
    (directory / "SHA256SUMS").write_text(
        "".join(f"{sha256(directory / name)}  {name}\n" for name in sorted(names)), encoding="utf-8")


def verify_checksums(directory, names):
    directory = Path(directory)
    expected = {name: sha256(directory / name) for name in names}
    found = {}
    for line in (directory / "SHA256SUMS").read_text(encoding="utf-8").splitlines():
        digest, name = line.split("  ", 1)
        if name in found or name not in expected or not re.fullmatch(r"[a-f0-9]{64}", digest):
            raise ValueError("Unexpected or duplicate checksum entry")
        found[name] = digest
    if found != expected:
        raise ValueError("Checksum mismatch or missing file")


def validate_archive(path, runtime, expected_version):
    with zipfile.ZipFile(path) as archive:
        if archive.testzip() is not None:
            raise ValueError("Invalid ZIP")
        names = archive.namelist()
        if any(name.startswith("/") or ".." in Path(name).parts or name.endswith(".sqlite") or name.endswith("settings.json") for name in names):
            raise ValueError("Unexpected archive path or user data")
        if runtime == "win-x64":
            prefix = "BlueforsMonitor/"
            for name in ("BlueforsMonitor.exe", "app/BlueforsAvalonia.exe"):
                data = archive.read(prefix + name)
                pe = struct.unpack_from("<I", data, 0x3C)[0]
                if data[pe:pe + 4] != b"PE\0\0" or struct.unpack_from("<H", data, pe + 4)[0] != 0x8664 or struct.unpack_from("<H", data, pe + 92)[0] != 2:
                    raise ValueError("Expected a Windows x64 GUI executable")
            for name in ("app/coreclr.dll", "app/hostfxr.dll", "app/e_sqlite3.dll", "app/libSkiaSharp.dll", "LICENSE", "THIRD_PARTY_NOTICES.md"):
                archive.getinfo(prefix + name)
            metadata = json.loads(archive.read(prefix + "BUILD-INFO.json"))
        else:
            prefix = "Bluefors Monitor.app/Contents/"
            info = plistlib.loads(archive.read(prefix + "Info.plist"))
            if info["CFBundleShortVersionString"] != expected_version or info["CFBundleVersion"] != expected_version:
                raise ValueError("macOS bundle version differs from VERSION")
            executable = archive.getinfo(prefix + "MacOS/BlueforsAvalonia")
            data = archive.read(executable)
            if data[:4] != b"\xcf\xfa\xed\xfe" or struct.unpack_from("<I", data, 4)[0] != 0x100000C:
                raise ValueError("Expected an arm64 Mach-O executable")
            if not ((executable.external_attr >> 16) & 0o111):
                raise ValueError("macOS executable permission was lost")
            for name in ("MacOS/libcoreclr.dylib", "MacOS/libhostfxr.dylib", "Resources/LICENSE", "Resources/THIRD_PARTY_NOTICES.md"):
                archive.getinfo(prefix + name)
            metadata = json.loads(archive.read(prefix + "Resources/BUILD-INFO.json"))
        if metadata["version"] != expected_version or metadata["runtime"] != runtime:
            raise ValueError("Embedded build metadata does not match package")
        return metadata


def build(runtime, output):
    value = version()
    output = Path(output).resolve()
    output.mkdir(parents=True, exist_ok=True)
    sdk = command(["dotnet", "--version"])
    if sdk != json.loads((ROOT / "global.json").read_text())["sdk"]["version"]:
        raise ValueError("Build SDK does not match global.json")
    compiled_version = command(["dotnet", "msbuild", "src/BlueforsMonitor.Desktop", "-getProperty:Version", "-nologo"])
    if compiled_version != value:
        raise ValueError("MSBuild version differs from VERSION")
    try:
        commit = command(["git", "rev-parse", "HEAD"])
        dirty = bool(command(["git", "status", "--porcelain"]))
    except subprocess.CalledProcessError:
        commit, dirty = "unknown", True
    with tempfile.TemporaryDirectory(prefix="bluefors-publish-") as scratch:
        scratch = Path(scratch)
        publish = scratch / "publish"
        run(["dotnet", "publish", "src/BlueforsMonitor.Desktop", "-c", "Release", "-r", runtime,
             "--self-contained", "true", "-p:RestoreLockedMode=true", "-p:UseSharedCompilation=false", "-o", str(publish)])
        runtime_config = json.loads((publish / "BlueforsAvalonia.runtimeconfig.json").read_text())
        metadata = {
            "version": value, "commit": commit, "dirty": dirty, "runtime": runtime,
            "dotnet_sdk": sdk, "frameworks": runtime_config["runtimeOptions"].get("includedFrameworks", []),
            "python": platform.python_version(), "build_os": platform.platform(),
            "runner_image": os.environ.get("ImageOS"), "runner_image_version": os.environ.get("ImageVersion"),
            "workflow_run": os.environ.get("GITHUB_RUN_ID"),
        }
        metadata_file = scratch / "BUILD-INFO.json"
        write_json(metadata_file, metadata)
        archive = output / archive_name(runtime, value)
        archive.unlink(missing_ok=True)
        if runtime == "win-x64":
            run([sys.executable, "scripts/package_windows.py", str(publish), str(archive), "--build-info", str(metadata_file)])
        else:
            if sys.platform != "darwin":
                raise ValueError("Build the macOS bundle on macOS to preserve bundle metadata")
            app = scratch / "Bluefors Monitor.app"
            contents = app / "Contents"
            shutil.copytree(publish, contents / "MacOS")
            resources = contents / "Resources"
            resources.mkdir()
            for name in ("LICENSE", "THIRD_PARTY_NOTICES.md"):
                shutil.copy2(ROOT / name, resources / name)
            shutil.copytree(ROOT / "licenses", resources / "licenses")
            shutil.copy2(metadata_file, resources / "BUILD-INFO.json")
            info = plistlib.loads((ROOT / "packaging/Info.plist.template").read_bytes())
            info.update(CFBundleShortVersionString=value, CFBundleVersion=value)
            (contents / "Info.plist").write_bytes(plistlib.dumps(info, sort_keys=False))
            run(["/usr/bin/ditto", "-c", "-k", "--sequesterRsrc", "--keepParent", str(app), str(archive)])
        validate_archive(archive, runtime, value)
        write_json(output / f"build-info-{runtime}.json", metadata)
        # Single-platform local builds have their own checksums; aggregation replaces these.
        checksums(output, [p.name for p in output.glob(f"BlueforsMonitor-{value}-*.zip")])
        print(archive)


def manifest(directory, commit):
    value = version()
    directory = Path(directory)
    if not re.fullmatch(r"[a-f0-9]{40}", commit):
        raise ValueError("Release requires an exact source commit")
    builds = []
    names = [archive_name(runtime, value) for runtime in RUNTIMES]
    for runtime, name in zip(RUNTIMES, names):
        info = validate_archive(directory / name, runtime, value)
        if info["dirty"] or info["commit"] != commit:
            raise ValueError("Release packages must come from the same clean commit")
        builds.append(info)
    write_json(directory / "BUILD-INFO.json", {"version": value, "commit": commit, "builds": builds})
    checksums(directory, names + ["BUILD-INFO.json"])
    return names + ["BUILD-INFO.json", "SHA256SUMS"]


def release_assets(directory, commit):
    directory = Path(directory)
    names = [archive_name(runtime) for runtime in RUNTIMES] + ["BUILD-INFO.json"]
    verify_checksums(directory, names)
    combined = json.loads((directory / "BUILD-INFO.json").read_text(encoding="utf-8"))
    if combined["commit"] != commit or combined["version"] != version():
        raise ValueError("Manifest version/commit mismatch")
    for runtime in RUNTIMES:
        info = validate_archive(directory / archive_name(runtime), runtime, version())
        if info["dirty"] or info["commit"] != commit or info not in combined["builds"]:
            raise ValueError("Embedded provenance differs from release manifest")
    return names + ["SHA256SUMS"]


def draft(directory, repository, commit):
    # Deliberately no publish command or --draft=false path in this workflow.
    if not re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", repository):
        raise ValueError("Invalid repository")
    directory = Path(directory).resolve()
    names = release_assets(directory, commit)
    tag = "v" + version()
    pages = json.loads(command(["gh", "api", f"repos/{repository}/releases?per_page=100", "--paginate", "--slurp"]))
    for existing in (release for page in pages for release in page):
        if existing["tag_name"] == tag:
            if not existing["draft"]:
                raise ValueError("This version is already published; it will not be changed")
            remote = {asset["name"]: asset.get("digest") for asset in existing["assets"]}
            expected = {name: "sha256:" + sha256(directory / name) for name in names}
            if existing["target_commitish"] == commit and remote == expected:
                print("Unchanged draft: " + existing["html_url"])
                return
            raise ValueError("An existing draft has different assets or source. It is preserved; bump VERSION for a new candidate, or explicitly remove the unneeded draft before retrying")
    remote_tags = command(["git", "ls-remote", "--tags", "origin", f"refs/tags/{tag}", f"refs/tags/{tag}^{{}}"])
    if remote_tags:
        resolved = remote_tags.splitlines()[-1].split()[0]
        if resolved != commit:
            raise ValueError("Existing version tag points to another commit")
    notes = f"# Bluefors Monitor {version()} — 待实机验收\n\n源码提交：`{commit}`\n\n"
    notes += (ROOT / "docs/release-notes.md").read_text(encoding="utf-8")
    with tempfile.TemporaryDirectory(prefix="bluefors-release-") as scratch:
        note_file = Path(scratch) / "notes.md"
        note_file.write_text(notes, encoding="utf-8")
        run(["gh", "release", "create", tag, *[str(directory / name) for name in names],
             "--repo", repository, "--target", commit, "--title", f"Bluefors Monitor {version()}",
             "--draft", "--prerelease", "--notes-file", str(note_file)])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="action", required=True)
    commands.add_parser("version")
    package = commands.add_parser("build")
    package.add_argument("--runtime", choices=RUNTIMES, required=True)
    package.add_argument("--output", type=Path, default=ROOT / "artifacts/release")
    for name in ("manifest", "validate", "draft"):
        item = commands.add_parser(name)
        item.add_argument("--directory", type=Path, required=True)
        item.add_argument("--commit", required=True)
        if name == "draft":
            item.add_argument("--repository", required=True)
    args = parser.parse_args()
    if args.action == "version":
        print(version())
    elif args.action == "build":
        build(args.runtime, args.output)
    elif args.action == "manifest":
        manifest(args.directory, args.commit)
    elif args.action == "validate":
        release_assets(args.directory, args.commit)
        print("PASS release versions, platforms, provenance and checksums")
    elif args.action == "draft":
        draft(args.directory, args.repository, args.commit)


if __name__ == "__main__":
    main()
