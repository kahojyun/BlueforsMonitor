import importlib.util
import json
from pathlib import Path
import plistlib
import struct
import tempfile
import unittest
from unittest.mock import patch
import zipfile

SPEC = importlib.util.spec_from_file_location("release", Path(__file__).resolve().parents[2] / "scripts/release.py")
release = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(release)
COMMIT = "a" * 40


def fixture(directory, runtime, *, commit=COMMIT, dirty=False, bundle_version=None, executable=True, deployment="aot"):
    value = release.version()
    info = {"version": value, "runtime": runtime, "commit": commit, "dirty": dirty, "deployment": deployment}
    archive = directory / release.archive_name(runtime, deployment=deployment)
    with zipfile.ZipFile(archive, "w") as z:
        if runtime == "win-x64":
            prefix = "BlueforsMonitor/"
            pe = bytearray(256)
            struct.pack_into("<I", pe, 0x3C, 64)
            pe[64:68] = b"PE\0\0"
            struct.pack_into("<H", pe, 68, 0x8664)
            struct.pack_into("<H", pe, 156, 2)
            for name in ("BlueforsMonitor.exe", "app/BlueforsMonitor.exe"):
                z.writestr(prefix + name, pe)
            for name in ("app/e_sqlite3.dll", "app/libSkiaSharp.dll", "LICENSE", "THIRD_PARTY_NOTICES.md"):
                z.writestr(prefix + name, "fixture")
            z.writestr(prefix + "BUILD-INFO.json", json.dumps(info))
        else:
            prefix = "Bluefors Monitor.app/Contents/"
            bundle = bundle_version or value
            z.writestr(prefix + "Info.plist", plistlib.dumps({"CFBundleShortVersionString": bundle, "CFBundleVersion": bundle, "CFBundleIdentifier": "io.github.kahojyun.blueforsmonitor", "CFBundleExecutable": "BlueforsMonitor", "CFBundleDisplayName": "Bluefors Monitor"}))
            entry = zipfile.ZipInfo(prefix + "MacOS/BlueforsMonitor")
            entry.external_attr = (0o100755 if executable else 0o100644) << 16
            z.writestr(entry, b"\xcf\xfa\xed\xfe" + struct.pack("<I", 0x100000C))
            for name in ("Resources/LICENSE", "Resources/THIRD_PARTY_NOTICES.md"):
                z.writestr(prefix + name, "fixture")
            z.writestr(prefix + "Resources/BUILD-INFO.json", json.dumps(info))
    return archive


class ReleaseChecks(unittest.TestCase):
    def setUp(self):
        self.scratch = tempfile.TemporaryDirectory()
        self.directory = Path(self.scratch.name)
        for runtime in release.RUNTIMES:
            fixture(self.directory, runtime)

    def tearDown(self):
        self.scratch.cleanup()

    def test_manifest_and_checksums(self):
        names = release.manifest(self.directory, COMMIT)
        self.assertEqual(set(names), set(release.release_assets(self.directory, COMMIT)))
        self.assertEqual(len(names), 4)

    def test_tampered_asset_fails_before_draft(self):
        release.manifest(self.directory, COMMIT)
        archive = self.directory / release.archive_name("win-x64")
        archive.write_bytes(archive.read_bytes() + b"modified")
        with patch.object(release, "command") as command, self.assertRaises(ValueError):
            release.draft(self.directory, "owner/repo", COMMIT)
        command.assert_not_called()

    def test_missing_platform_rejected(self):
        (self.directory / release.archive_name("osx-arm64")).unlink()
        with self.assertRaises(FileNotFoundError):
            release.manifest(self.directory, COMMIT)

    def test_mixed_commits_and_dirty_builds_rejected(self):
        for options in ({"commit": "b" * 40}, {"dirty": True}):
            with self.subTest(options=options):
                fixture(self.directory, "win-x64", **options)
                with self.assertRaises(ValueError):
                    release.manifest(self.directory, COMMIT)

    def test_mac_version_and_executable_mode(self):
        for options in ({"bundle_version": "9.9.9"}, {"executable": False}):
            with self.subTest(options=options):
                fixture(self.directory, "osx-arm64", **options)
                with self.assertRaises(ValueError):
                    release.manifest(self.directory, COMMIT)

    def test_published_release_and_changed_draft_are_never_overwritten(self):
        release.manifest(self.directory, COMMIT)
        for draft in (False, True):
            existing = {"tag_name": "v" + release.version(), "draft": draft, "target_commitish": "b" * 40, "assets": []}
            with patch.object(release, "command", return_value=json.dumps([[existing]])), patch.object(release, "run") as run:
                with self.assertRaises(ValueError):
                    release.draft(self.directory, "owner/repo", COMMIT)
                run.assert_not_called()

    def test_create_is_always_a_draft_at_exact_commit(self):
        release.manifest(self.directory, COMMIT)
        with patch.object(release, "command", side_effect=["[[]]", ""]), patch.object(release, "run") as run:
            release.draft(self.directory, "owner/repo", COMMIT)
        args = run.call_args.args[0]
        self.assertIn("--draft", args)
        self.assertIn("--prerelease", args)
        self.assertEqual(args[args.index("--target") + 1], COMMIT)
        self.assertNotIn("--draft=false", args)

    def test_wrong_existing_tag_is_rejected(self):
        release.manifest(self.directory, COMMIT)
        with patch.object(release, "command", side_effect=["[[]]", "b" * 40 + "\trefs/tags/v" + release.version()]), patch.object(release, "run") as run:
            with self.assertRaises(ValueError):
                release.draft(self.directory, "owner/repo", COMMIT)
            run.assert_not_called()

    def test_release_cannot_accept_jit_metadata(self):
        for runtime in release.RUNTIMES:
            archive = fixture(self.directory, runtime, deployment="jit")
            with self.assertRaises(ValueError):
                release.validate_archive(archive, runtime, release.version())
        self.assertNotEqual(release.archive_name("win-x64"), release.archive_name("win-x64", deployment="jit"))
        self.assertFalse(release.archive_name("win-x64").endswith("-aot.zip"))

    def test_version_input_is_numeric_and_path_safe(self):
        for value in ("../other", "0.5", "v0.5.0", "0.05.0", "0.5.0\nextra"):
            (self.directory / "VERSION").write_text(value)
            with self.assertRaises(ValueError):
                release.version(self.directory)


if __name__ == "__main__":
    unittest.main()
