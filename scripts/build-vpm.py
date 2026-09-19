#!/usr/bin/env python3
"""Build a deterministic VPM archive from tracked package files."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import subprocess
import sys
import re
from typing import Any
from zipfile import ZIP_DEFLATED, ZipFile, ZipInfo


PACKAGE_ID = "dev.nerdrx.nxsg"
DEFAULT_OUTPUT = Path("work/vpm")
DEFAULT_LISTING = "https://nerdrx.github.io/nxsg/index.json"
SEMVER = re.compile(
    r"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)"
    r"(?:-(?:0|[1-9]\d*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9]\d*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*))*)?"
    r"(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$"
)


def _json_bytes(value: Any) -> bytes:
    return (json.dumps(value, indent=2, sort_keys=True, ensure_ascii=False) + "\n").encode("utf-8")


def _tracked_files(repo: Path, package: Path) -> list[Path]:
    relative_root = package.relative_to(repo).as_posix()
    result = subprocess.run(
        ["git", "-C", str(repo), "ls-files", "--stage", "-z", "--", relative_root],
        check=True,
        capture_output=True,
    )
    paths: list[Path] = []
    for record in result.stdout.decode().split("\0"):
        if not record:
            continue
        metadata, relative_name = record.split("\t", 1)
        mode = metadata.split(" ", 1)[0]
        if mode == "120000" or (repo / relative_name).is_symlink():
            raise ValueError(f"Refusing symlink in package: {relative_name}")
        path = repo / relative_name
        if not path.is_file() or not path.is_relative_to(package):
            raise ValueError(f"Invalid tracked package file: {relative_name}")
        paths.append(path)
    if not paths:
        raise ValueError(f"No tracked files under {package}")
    return sorted(paths, key=lambda path: path.relative_to(package).as_posix())


def _manifest(package: Path) -> dict[str, Any]:
    path = package / "package.json"
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise ValueError(f"Invalid package manifest: {path}") from exc
    if not isinstance(value, dict) or value.get("name") != PACKAGE_ID:
        raise ValueError(f"Manifest name must be {PACKAGE_ID!r}")
    version = value.get("version")
    if not isinstance(version, str) or not SEMVER.fullmatch(version):
        raise ValueError(f"Manifest version must be valid SemVer: {version!r}")
    release_url = value.get("url")
    if not isinstance(release_url, str) or not release_url.startswith("https://"):
        raise ValueError("Manifest url must use https://")
    expected_suffix = f"/{PACKAGE_ID}-{version}.zip"
    if not release_url.endswith(expected_suffix):
        raise ValueError(f"Manifest url must end with {expected_suffix}")
    return value


def _archive(package: Path, files: list[Path], destination: Path) -> str:
    destination.parent.mkdir(parents=True, exist_ok=True)
    with ZipFile(destination, "w", compression=ZIP_DEFLATED, compresslevel=9) as archive:
        for path in files:
            name = PurePosixPath(path.relative_to(package).as_posix()).as_posix()
            info = ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
            info.compress_type = ZIP_DEFLATED
            info.create_system = 3
            info.external_attr = 0o100644 << 16
            archive.writestr(info, path.read_bytes())
    digest = hashlib.sha256(destination.read_bytes()).hexdigest()
    return digest


def _load_listing(path: Path) -> dict[str, Any]:
    if not path.exists():
        return {}
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise ValueError(f"Invalid VPM listing: {path}") from exc
    if not isinstance(value, dict):
        raise ValueError("VPM listing must be an object")
    return value


def build(
    repo: Path,
    output: Path = DEFAULT_OUTPUT,
    listing_path: Path | None = None,
    listing_url: str = DEFAULT_LISTING,
) -> tuple[Path, Path]:
    package = repo / "Packages" / PACKAGE_ID
    manifest = _manifest(package)
    version = manifest["version"]
    files = _tracked_files(repo, package)
    output.mkdir(parents=True, exist_ok=True)
    archive_path = output / f"{PACKAGE_ID}-{version}.zip"
    digest = _archive(package, files, archive_path)
    standalone_path = output / "package.json"
    standalone_path.write_bytes(_json_bytes(manifest))

    listing_path = listing_path or (repo / "vpm" / "index.json")
    listing = _load_listing(listing_path)
    packages = listing.setdefault("packages", {})
    if not isinstance(packages, dict):
        raise ValueError("VPM listing packages must be an object")
    package_listing = packages.setdefault(PACKAGE_ID, {})
    versions = package_listing.setdefault("versions", {})
    if not isinstance(versions, dict):
        raise ValueError("VPM package versions must be an object")
    entry = dict(manifest)
    entry["zipSHA256"] = digest
    existing = versions.get(version)
    if existing is not None and existing != entry:
        raise ValueError(f"Existing version {version} differs; refusing overwrite")
    versions[version] = entry
    listing.update({"name": "NXSG", "id": f"{PACKAGE_ID}.listing", "url": listing_url, "author": "nerdrx"})
    listing_path.parent.mkdir(parents=True, exist_ok=True)
    listing_path.write_bytes(_json_bytes(listing))
    return archive_path, listing_path


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--listing", type=Path, default=None, help="VPM index.json path")
    parser.add_argument("--listing-url", default=DEFAULT_LISTING)
    parser.add_argument("--check", action="store_true", help="Build and verify archive/listing")
    args = parser.parse_args(argv)
    try:
        repo = Path(__file__).resolve().parents[1]
        output = args.output if args.output.is_absolute() else repo / args.output
        listing = args.listing if args.listing is None or args.listing.is_absolute() else repo / args.listing
        archive, listing = build(repo, output, listing, args.listing_url)
        if args.check:
            with ZipFile(archive) as value:
                if "package.json" not in value.namelist():
                    raise ValueError("Archive missing package.json")
            json.loads(listing.read_text(encoding="utf-8"))
    except (OSError, subprocess.CalledProcessError, ValueError) as exc:
        print(f"build-vpm: {exc}", file=sys.stderr)
        return 1
    print(archive)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
