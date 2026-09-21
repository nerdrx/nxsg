#!/usr/bin/env python3
"""Verify the actual public archives advertised by a VPM listing (stdlib only)."""
import hashlib
import io
import json
from pathlib import Path, PurePosixPath
import sys
from urllib.parse import urlparse
from urllib.request import Request, urlopen
import zipfile

LIMIT = 32 * 1024 * 1024
IMPORTER_GUID = '31f27f7b58a03015f8c4788064374330'
LEGACY_SAMPLE_META_VERSIONS = frozenset(
    f'0.1.0-alpha.{number}' for number in range(1, 9)
)


def requires_sample_metas(version):
    """Require sample metadata for new archives; preserve immutable alpha.1-alpha.8."""
    return version not in LEGACY_SAMPLE_META_VERSIONS


def verify_sample_metas(names, read):
    """Require Unity metadata for every shipped sample asset."""
    samples = {name for name in names if name.startswith('Samples~/')}
    assets = {name for name in samples if not name.endswith('.meta')}
    metas = {name for name in samples if name.endswith('.meta')}
    expected = {f'{name}.meta' for name in assets}
    missing = sorted(expected - metas)
    orphaned = sorted(metas - expected)
    if missing:
        raise ValueError('Sample metadata missing: ' + ', '.join(missing))
    if orphaned:
        raise ValueError('Orphaned sample metadata: ' + ', '.join(orphaned))
    for name in sorted(assets):
        if name.endswith('.nxsg'):
            meta = read(f'{name}.meta').decode('utf-8')
            if 'ScriptedImporter:\n' not in meta or f'guid: {IMPORTER_GUID}' not in meta:
                raise ValueError('Invalid NXSG sample metadata: ' + name)
        elif name.endswith('.md'):
            meta = read(f'{name}.meta').decode('utf-8')
            if 'TextScriptImporter:\n' not in meta:
                raise ValueError('Invalid text sample metadata: ' + name)


def fetch(url):
    if urlparse(url).scheme != 'https':
        raise ValueError('Package downloads must use HTTPS')
    with urlopen(Request(url, headers={'User-Agent': 'NXSG-package-check'}), timeout=60) as response:
        if urlparse(response.url).scheme != 'https':
            raise ValueError('Insecure download redirect')
        data = response.read(LIMIT + 1)
    if len(data) > LIMIT:
        raise ValueError('Package exceeds 32 MiB verification limit')
    return data


def verify(listing):
    for field in ('name', 'id', 'url', 'author', 'packages'):
        if not listing.get(field):
            raise ValueError('Missing listing field: ' + field)
    for name, package in listing['packages'].items():
        for version, manifest in package['versions'].items():
            if manifest['name'] != name or manifest['version'] != version:
                raise ValueError('Package key/manifest mismatch')
            data = fetch(manifest['url'])
            if hashlib.sha256(data).hexdigest() != manifest['zipSHA256']:
                raise ValueError('Archive checksum mismatch: ' + version)
            with zipfile.ZipFile(io.BytesIO(data)) as archive:
                names = archive.namelist()
                if len(set(names)) != len(names):
                    raise ValueError('Duplicate archive entries')
                if sum(info.file_size for info in archive.infolist()) > LIMIT * 2:
                    raise ValueError('Expanded package too large')
                for info in archive.infolist():
                    path = PurePosixPath(info.filename)
                    if path.is_absolute() or '..' in path.parts or '\\' in info.filename or (info.external_attr >> 16) & 0o170000 == 0o120000:
                        raise ValueError('Unsafe archive entry')
                if archive.testzip() is not None:
                    raise ValueError('Archive CRC failure')
                packaged = json.loads(archive.read('package.json'))
                expected = {k: v for k, v in manifest.items() if k != 'zipSHA256'}
                if packaged != expected:
                    raise ValueError('Listing differs from packaged manifest')
                if not any(n.startswith('Editor/') and n.endswith('.cs') for n in names):
                    raise ValueError('Editor sources missing')
                if requires_sample_metas(version):
                    verify_sample_metas(names, archive.read)
            print(f'Verified {name}@{version}: SHA-256, ZIP structure, package manifest ({len(data)} bytes)')


if __name__ == '__main__':
    source = sys.argv[1] if len(sys.argv) > 1 else 'vpm/index.json'
    verify(json.loads(fetch(source) if source.startswith('https://') else Path(source).read_bytes()))
