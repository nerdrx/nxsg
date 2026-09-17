#!/usr/bin/env python3
"""Install the pinned VRChat SDK packages into the clean NXSG fixture.

This script is deliberately explicit: Unity opening a project never downloads
or installs SDK content. Run it from the NXSG package root when the pinned SDK
fixture is wanted. Existing package directories must be either absent or an
fixture previously installed by this script. Unity may generate XR settings
or remove orphan metadata; those later changes are reported and preserved.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import shutil
import tempfile
from typing import Any
from urllib.request import urlopen
from zipfile import ZipFile, ZipInfo


ROOT = Path(__file__).resolve().parents[1]
PROJECT_PACKAGES = ROOT / "DevProject" / "Packages"
DEFAULT_WORK = ROOT / "work" / "sdk"
MARKER_NAME = ".nxsg-vrchat-fixture.json"

PACKAGES = {
    "com.vrchat.base": {
        "version": "3.10.5",
        "url": "https://github.com/vrchat/packages/releases/download/3.10.5/com.vrchat.base-3.10.5.zip",
        "sha256": "fbfb3e7a38778dcb55d7a860286819e6f0726d10d5039f61474bd1b9c629029e",
    },
    "com.vrchat.avatars": {
        "version": "3.10.5",
        "url": "https://github.com/vrchat/packages/releases/download/3.10.5/com.vrchat.avatars-3.10.5.zip",
        "sha256": "03bdea0c24257070f0e7a73c9033742a1ce0f67463b12a6c2ad29608b1f33a77",
    },
}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def download_verified(spec: dict[str, str], path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.exists():
        actual = sha256(path)
        if actual != spec["sha256"]:
            raise RuntimeError(
                f"Existing archive has wrong SHA-256: {path}\n"
                f"expected {spec['sha256']}\nactual   {actual}\n"
                "Review or remove it manually; the script will not overwrite it."
            )
        return

    partial = path.with_name(path.name + ".part")
    if partial.exists():
        raise RuntimeError(f"Refusing to reuse an unfinished download: {partial}")
    print(f"Downloading {spec['url']}")
    try:
        with urlopen(spec["url"], timeout=120) as response, partial.open("xb") as out:
            for block in iter(lambda: response.read(1024 * 1024), b""):
                out.write(block)
        actual = sha256(partial)
        if actual != spec["sha256"]:
            raise RuntimeError(
                f"Downloaded archive has wrong SHA-256: {path}\n"
                f"expected {spec['sha256']}\nactual   {actual}"
            )
        os.replace(partial, path)
    except Exception:
        partial.unlink(missing_ok=True)
        raise


def safe_member_path(name: str) -> PurePosixPath:
    if not name or "\\" in name:
        raise RuntimeError(f"Unsafe archive member path: {name!r}")
    path = PurePosixPath(name)
    if path.is_absolute() or any(part in ("", ".", "..") for part in path.parts):
        raise RuntimeError(f"Unsafe archive member path: {name!r}")
    return path


def extract_package(archive: Path, expected_name: str, staging: Path) -> Path:
    with ZipFile(archive) as zfile:
        for info in zfile.infolist():
            path = safe_member_path(info.filename)
            mode = (info.external_attr >> 16) & 0o170000
            if mode == 0o120000:
                raise RuntimeError(f"Refusing symlink in archive: {info.filename}")
            destination = staging.joinpath(*path.parts)
            if not destination.resolve().is_relative_to(staging.resolve()):
                raise RuntimeError(f"Archive member escapes staging directory: {info.filename}")
            if info.is_dir():
                destination.mkdir(parents=True, exist_ok=True)
                continue
            destination.parent.mkdir(parents=True, exist_ok=True)
            with zfile.open(info) as source, destination.open("xb") as target:
                shutil.copyfileobj(source, target)

    manifests = []
    for manifest_path in staging.rglob("package.json"):
        try:
            manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError):
            continue
        if manifest.get("name") == expected_name:
            manifests.append((manifest_path, manifest))
    if len(manifests) != 1:
        raise RuntimeError(f"Expected one {expected_name}/package.json, found {len(manifests)}")
    manifest_path, manifest = manifests[0]
    if manifest.get("version") != PACKAGES[expected_name]["version"]:
        raise RuntimeError(f"Unexpected {expected_name} version: {manifest.get('version')!r}")
    if expected_name == "com.vrchat.avatars":
        required = manifest.get("vpmDependencies", {}).get("com.vrchat.base")
        if required != PACKAGES["com.vrchat.base"]["version"]:
            raise RuntimeError(f"Avatars package requires base {required!r}, not 3.10.5")
    return manifest_path.parent


def tree_digest(package_dir: Path) -> str:
    digest = hashlib.sha256()
    for path in sorted(package_dir.rglob("*")):
        if path.name == MARKER_NAME:
            continue
        relative = path.relative_to(package_dir).as_posix().encode("utf-8")
        if path.is_symlink():
            raise RuntimeError(f"Fixture contains unsupported symlink: {path}")
        if path.is_dir():
            continue
        if not path.is_file():
            raise RuntimeError(f"Fixture contains unsupported entry: {path}")
        digest.update(relative + b"\0")
        with path.open("rb") as stream:
            for block in iter(lambda: stream.read(1024 * 1024), b""):
                digest.update(block)
        digest.update(b"\0")
    return digest.hexdigest()


def install_package(name: str, spec: dict[str, str], source_dir: Path) -> str:
    destination = PROJECT_PACKAGES / name
    marker_path = destination / MARKER_NAME
    if destination.exists():
        if not marker_path.is_file() or marker_path.is_symlink():
            raise RuntimeError(f"Refusing to touch unowned package directory: {destination}")
        try:
            marker: dict[str, Any] = json.loads(marker_path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as exc:
            raise RuntimeError(f"Invalid fixture marker: {marker_path}") from exc
        if marker.get("package") != name or marker.get("archiveSha256") != spec["sha256"]:
            raise RuntimeError(f"Fixture marker does not match pinned archive: {marker_path}")
        actual = tree_digest(destination)
        if actual != marker.get("treeSha256"):
            manifest = json.loads((destination / "package.json").read_text(encoding="utf-8"))
            if manifest.get("name") != name or manifest.get("version") != spec["version"]:
                raise RuntimeError(f"Installed package version changed; refusing overwrite: {destination}")
            return "preserved local changes (installed tree differs from verified extraction)"
        return "unchanged"

    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copytree(source_dir, destination, symlinks=False)
    marker = {
        "package": name,
        "version": spec["version"],
        "archiveSha256": spec["sha256"],
        "source": spec["url"],
        "treeSha256": tree_digest(destination),
    }
    marker_path.write_text(json.dumps(marker, indent=2) + "\n", encoding="utf-8")
    return "installed"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--work-dir", type=Path, default=DEFAULT_WORK,
                        help="download/cache directory (default: %(default)s)")
    args = parser.parse_args()
    args.work_dir = args.work_dir.expanduser().resolve()
    args.work_dir.mkdir(parents=True, exist_ok=True)
    statuses = []
    with tempfile.TemporaryDirectory(prefix="nxsg-vrchat-", dir=args.work_dir) as temp_name:
        staging_root = Path(temp_name)
        for name, spec in PACKAGES.items():
            archive = args.work_dir / f"{name}-{spec['version']}.zip"
            download_verified(spec, archive)
            package_source = extract_package(archive, name, staging_root / name)
            statuses.append((name, install_package(name, spec, package_source)))
    for name, status in statuses:
        print(f"{status}: {name} {PACKAGES[name]['version']}")
    print(f"Fixture ready in {PROJECT_PACKAGES}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
