from __future__ import annotations

import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import shutil
import tempfile
import unittest
from zipfile import ZipFile


SCRIPT = Path(__file__).parents[2] / "scripts" / "build-vpm.py"
SPEC = importlib.util.spec_from_file_location("build_vpm", SCRIPT)
assert SPEC and SPEC.loader
builder = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(builder)
VERIFY = Path(__file__).parents[2] / "scripts" / "verify-vpm.py"
VERIFY_SPEC = importlib.util.spec_from_file_location("verify_vpm", VERIFY)
assert VERIFY_SPEC and VERIFY_SPEC.loader
verifier = importlib.util.module_from_spec(VERIFY_SPEC)
VERIFY_SPEC.loader.exec_module(verifier)


def repo(tmp_path: Path, version: str = "1.0.0") -> Path:
    root = tmp_path / "repo"
    package = root / "Packages" / builder.PACKAGE_ID
    package.mkdir(parents=True)
    manifest = {
        "name": builder.PACKAGE_ID,
        "version": version,
        "url": f"https://example.test/releases/{builder.PACKAGE_ID}-{version}.zip",
    }
    (package / "package.json").write_text(json.dumps(manifest) + "\n")
    (package / "package.json.meta").write_text("meta\n")
    (package / "Core.cs").write_text("tracked\n")
    subprocess.run(["git", "init", "-q", str(root)], check=True)
    subprocess.run(["git", "-C", str(root), "add", "."], check=True)
    subprocess.run(["git", "-C", str(root), "-c", "user.name=test", "-c", "user.email=test@example.test", "commit", "-qm", "fixture"], check=True)
    return root


class VpmBuilderTests(unittest.TestCase):
    def test_sample_metadata_rule_grandfathers_only_existing_alphas(self) -> None:
        for number in range(1, 9):
            self.assertFalse(verifier.requires_sample_metas(f"0.1.0-alpha.{number}"))
        self.assertTrue(verifier.requires_sample_metas("0.1.0-alpha.9"))
        self.assertTrue(verifier.requires_sample_metas("0.1.0"))

    def test_requires_metadata_for_every_sample_asset(self) -> None:
        files = {
            "Editor/Source.cs": b"source",
            "Samples~/README.md": b"docs",
            "Samples~/README.md.meta": b"TextScriptImporter:\n",
            "Samples~/Example.nxsg": b"{}",
            "Samples~/Example.nxsg.meta": (
                b"ScriptedImporter:\n"
                b"guid: 31f27f7b58a03015f8c4788064374330\n"
            ),
        }
        with self.assertRaisesRegex(ValueError, "Sample metadata missing"):
            verifier.verify_sample_metas(
                ["Samples~/README.md", "Samples~/README.md.meta", "Samples~/Example.nxsg"],
                files.__getitem__,
            )
        verifier.verify_sample_metas(files, files.__getitem__)

    def test_rejects_dirty_tracked_package(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = repo(Path(directory))
            (root / "Packages" / builder.PACKAGE_ID / "Core.cs").write_text("changed\n")
            with self.assertRaisesRegex(ValueError, "uncommitted tracked changes"):
                builder.build(root, Path(directory) / "output", Path(directory) / "index.json")

    def test_rejects_untracked_package_file(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = repo(Path(directory))
            (root / "Packages" / builder.PACKAGE_ID / "ForgottenNode.cs").write_text("new node\n")
            with self.assertRaisesRegex(ValueError, "untracked files"):
                builder.build(root, Path(directory) / "output", Path(directory) / "index.json")

    def test_rejected_version_preserves_published_artifacts(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = repo(Path(directory))
            output, listing = Path(directory) / "output", root / "vpm" / "index.json"
            archive, _ = builder.build(root, output, listing)
            before = archive.read_bytes(), listing.read_bytes(), (output / "package.json").read_bytes()
            (root / "Packages" / builder.PACKAGE_ID / "Core.cs").write_text("changed\n")
            subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
            subprocess.run(["git", "-C", str(root), "-c", "user.name=test", "-c", "user.email=test@example.test", "commit", "-qm", "changed code"], check=True)
            with self.assertRaisesRegex(ValueError, "refusing overwrite"):
                builder.build(root, output, listing)
            self.assertEqual(before, (archive.read_bytes(), listing.read_bytes(), (output / "package.json").read_bytes()))

    def test_deterministic_archive_and_meta_root(self) -> None:
        with __import__("tempfile").TemporaryDirectory() as directory:
            root = repo(Path(directory))
            output = Path(directory) / "vpm"
            listing = root / "vpm" / "index.json"
            archive, listing = builder.build(root, output, listing)
            first = archive.read_bytes()
            shutil.rmtree(output)
            builder.build(root, output, listing)
            self.assertEqual(archive.read_bytes(), first)
            with ZipFile(archive) as value:
                self.assertEqual(value.namelist(), ["Core.cs", "package.json", "package.json.meta"])
                self.assertTrue(all(entry.date_time == (1980, 1, 1, 0, 0, 0) for entry in value.infolist()))
            data = json.loads(listing.read_text())
            entry = data["packages"][builder.PACKAGE_ID]["versions"]["1.0.0"]
            self.assertEqual(entry["zipSHA256"], hashlib.sha256(first).hexdigest())


    def test_preserves_old_versions_and_rejects_changed_same_version(self) -> None:
        with __import__("tempfile").TemporaryDirectory() as directory:
            root = repo(Path(directory))
            output = Path(directory) / "output"
            listing = root / "vpm" / "index.json"
            builder.build(root, output, listing)
            data = json.loads(listing.read_text())
            data["packages"][builder.PACKAGE_ID]["versions"]["0.9.0"] = {"version": "0.9.0"}
            listing.write_text(json.dumps(data))
            shutil.rmtree(output)
            builder.build(root, output, listing)
            self.assertIn("0.9.0", json.loads(listing.read_text())["packages"][builder.PACKAGE_ID]["versions"])
            self.assertNotIn("listing", json.loads(listing.read_text()))
            self.assertEqual(json.loads(listing.read_text())["id"], "dev.nerdrx.nxsg.listing")
            data = json.loads(listing.read_text())
            data["packages"][builder.PACKAGE_ID]["versions"]["1.0.0"]["url"] = "changed"
            listing.write_text(json.dumps(data))
            with self.assertRaisesRegex(ValueError, "differs"):
                builder.build(root, output, listing)


    def test_rejects_tracked_symlink(self) -> None:
        with __import__("tempfile").TemporaryDirectory() as directory:
            root = repo(Path(directory))
            package = root / "Packages" / builder.PACKAGE_ID
            (package / "link").symlink_to("Core.cs")
            subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
            subprocess.run(["git", "-C", str(root), "-c", "user.name=test", "-c", "user.email=test@example.test", "commit", "-qm", "symlink"], check=True)
            with self.assertRaisesRegex(ValueError, "symlink"):
                builder.build(root, Path(directory) / "vpm", root / "listing.json")

    def test_rejects_invalid_semver(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = repo(Path(directory), "1.0.0/evil")
            with self.assertRaisesRegex(ValueError, "valid SemVer"):
                builder.build(root, Path(directory) / "vpm", Path(directory) / "index.json")


if __name__ == "__main__":
    unittest.main()
