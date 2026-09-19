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
            print(f'Verified {name}@{version}: SHA-256, ZIP structure, package manifest ({len(data)} bytes)')


if __name__ == '__main__':
    source = sys.argv[1] if len(sys.argv) > 1 else 'vpm/index.json'
    verify(json.loads(fetch(source) if source.startswith('https://') else Path(source).read_bytes()))
