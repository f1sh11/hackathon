#!/usr/bin/env python3
"""Build/verify an offline PUBLIC travel-data migration bundle; never deploys or reads credentials."""
import argparse
import csv
import hashlib
import io
import json
import math
import re
import sys
import zipfile
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DATA_FILES = ('travel-records.csv', 'hidden-gems.csv', 'osm-map-places.csv')
ASSETS = ('README.md', 'restore_source.py', 'restore.sql', 'register.template.sql',
          'weather.sql', 'insights.sql', 'verify.sql', 'grant-reader.sql', 'LICENSES.md')
HEADERS = ['dataset', 'record_id', 'name', 'category', 'lat', 'lng', 'source', 'observed_at', 'payload']
PLACE_HEADERS = ['id', 'name', 'lat', 'lng', 'payload']
ALLOWED = {'data/' + f for f in DATA_FILES} | set(ASSETS) | {'verify_bundle.py'}
MAX_BYTES = 100 * 1024 * 1024
SECRET_KEYS = {'password', 'passwd', 'token', 'accesstoken', 'refreshtoken', 'apikey',
               'authorization', 'secret', 'privatekey', 'users', 'userpreferences', 'trips', 'sessions'}

def public_payload(value):
    if isinstance(value, dict):
        for key, child in value.items():
            if re.sub('[^a-z]', '', key.lower()) in SECRET_KEYS:
                raise ValueError('Private field found in public export')
            public_payload(child)
    elif isinstance(value, list):
        for child in value:
            public_payload(child)
    elif isinstance(value, str):
        if re.search(r'-----BEGIN .*PRIVATE KEY|AKIA[0-9A-Z]{16}|gh[pousr]_[A-Za-z0-9]{20,}', value):
            raise ValueError('Credential-like value found in public export')

def validate_data(files):
    counts = Counter()
    for filename in DATA_FILES:
        reader = csv.DictReader(io.StringIO(files['data/' + filename].decode('utf-8'), newline=''))
        if reader.fieldnames != (HEADERS if filename == DATA_FILES[0] else PLACE_HEADERS):
            raise ValueError('Unexpected CSV columns: ' + filename)
        seen = set()
        for row in reader:
            if None in row or any(v is None for v in row.values()):
                raise ValueError('Malformed CSV row')
            payload = json.loads(row['payload'])
            if not isinstance(payload, dict):
                raise ValueError('Payload must be an object')
            public_payload(payload)
            if filename == DATA_FILES[0]:
                dataset = row['dataset']
                if dataset not in ('poi', 'catalog', 'map_layers', 'metadata'):
                    raise ValueError('Only static public datasets may be bundled; no weather')
                key = (dataset, row['record_id'])
                if dataset == 'poi' and not row['record_id'].startswith('overture:'):
                    raise ValueError('Unexpected POI provenance')
                if dataset in ('poi', 'catalog') and payload.get('id') != row['record_id']:
                    raise ValueError('Payload ID mismatch')
            else:
                dataset = 'hidden_gems' if filename == DATA_FILES[1] else 'osm_places'
                key = row['id']
                pattern = r'gem:[a-f0-9]{24}' if dataset == 'hidden_gems' else r'osm:(node|way|relation):[0-9]+'
                if not re.fullmatch(pattern, key) or payload.get('id') != key:
                    raise ValueError('Unexpected public place ID')
                if dataset == 'hidden_gems' and not payload.get('guide', {}).get('sourceUrl'):
                    raise ValueError('Missing community attribution')
                if dataset == 'osm_places' and not payload.get('sourceLicense'):
                    raise ValueError('Missing OSM license')
            if key in seen:
                raise ValueError('Duplicate record')
            seen.add(key)
            for field, bound in (('lat', 90), ('lng', 180)):
                if row[field]:
                    value = float(row[field])
                    if not math.isfinite(value) or abs(value) > bound:
                        raise ValueError('Invalid coordinate')
                    if field in payload and payload[field] is not None and float(payload[field]) != value:
                        raise ValueError('Payload coordinate mismatch')
            counts[dataset] += 1
    required = {'poi', 'catalog', 'map_layers', 'metadata', 'hidden_gems', 'osm_places'}
    if set(counts) != required:
        raise ValueError('Missing nonempty public dataset')
    return dict(sorted(counts.items()))

def digest(data):
    return hashlib.sha256(data).hexdigest()

def verify_files(files):
    if set(files) != ALLOWED | {'manifest.json'}:
        raise ValueError('Unexpected or missing bundle files')
    manifest = json.loads(files['manifest.json'])
    if manifest.get('schemaVersion') != 1 or set(manifest.get('files', {})) != ALLOWED:
        raise ValueError('Invalid manifest')
    for name in ALLOWED:
        if manifest['files'][name] != {'sha256': digest(files[name]), 'bytes': len(files[name])}:
            raise ValueError('Checksum mismatch: ' + name)
    if manifest['counts'] != validate_data(files):
        raise ValueError('Record counts do not match manifest')
    return manifest

def verify(path):
    if path.is_dir():
        paths = list(path.rglob('*'))
        if any(p.is_symlink() for p in paths):
            raise ValueError('Symlinks are not allowed')
        paths = [p for p in paths if p.is_file()]
        if sum(p.stat().st_size for p in paths) > MAX_BYTES:
            raise ValueError('Bundle too large')
        files = {p.relative_to(path).as_posix(): p.read_bytes() for p in paths}
    else:
        with zipfile.ZipFile(path) as archive:
            entries = archive.infolist()
            names = [p.filename for p in entries]
            if len(names) != len(set(names)) or set(names) != ALLOWED | {'manifest.json'}:
                raise ValueError('Unsafe or unexpected archive paths')
            if sum(p.file_size for p in entries) > MAX_BYTES:
                raise ValueError('Bundle too large')
            if any((p.external_attr >> 16) & 0o170000 == 0o120000 for p in entries):
                raise ValueError('Archive symlink is not allowed')
            files = {name: archive.read(name) for name in names}
    return verify_files(files)

def build(source, output, assets=None):
    assets = assets or ROOT / 'infra/zetaris/migration'
    files = {}
    for filename in DATA_FILES:
        path = source / filename
        if path.is_symlink():
            raise ValueError('Source symlink is not allowed')
        if path.stat().st_size > MAX_BYTES:
            raise ValueError('Input too large')
        files['data/' + filename] = path.read_bytes()
    counts = validate_data(files)
    for filename in ASSETS:
        files[filename] = (assets / filename).read_bytes()
    files['verify_bundle.py'] = Path(__file__).read_bytes()
    manifest = {'schemaVersion': 1, 'generatedAt': datetime.now(timezone.utc).isoformat(),
                'kind': 'sydneybuddy-public-travel', 'testedLocalZetarisVersion': '2.4.3.1',
                'weather': 'live REST configuration only; no forecast records or cache',
                'counts': counts,
                'files': {n: {'sha256': digest(b), 'bytes': len(b)} for n, b in sorted(files.items())}}
    files['manifest.json'] = (json.dumps(manifest, ensure_ascii=False, indent=2) + '\n').encode()
    verify_files(files)
    if sum(map(len, files.values())) > MAX_BYTES:
        raise ValueError('Bundle too large')
    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open('xb') as handle:
        with zipfile.ZipFile(handle, 'w', compression=zipfile.ZIP_DEFLATED) as archive:
            for name, data in sorted(files.items()):
                archive.writestr(name, data)
    verify(output)
    checksum = digest(output.read_bytes())
    output.with_suffix(output.suffix + '.sha256').write_text(checksum + '  ' + output.name + '\n')
    return manifest

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-dir', type=Path, default=ROOT / 'infra/zetaris/data')
    parser.add_argument('--output', type=Path, default=ROOT / ('infra/zetaris/bundles/travel-' + datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%S%fZ') + '.zip'))
    parser.add_argument('--verify', type=Path, help='Verify an archive OR an extracted directory; do not deploy')
    args = parser.parse_args()
    result = verify(args.verify) if args.verify else build(args.source_dir, args.output)
    print(json.dumps({'status': 'verified', 'path': str(args.verify or args.output), 'counts': result['counts']}, ensure_ascii=False))

if __name__ == '__main__':
    try:
        main()
    except (ValueError, OSError, KeyError, json.JSONDecodeError, zipfile.BadZipFile, csv.Error) as error:
        print('Bundle failed: ' + str(error), file=sys.stderr)
        sys.exit(1)
