#!/usr/bin/env python3
"""Load reviewed Sydney public transport routes into local PostgreSQL + Local Zetaris and/or Cloud Zetaris.
Only factual public timetable route structures and station coordinates; no credentials or private data.
"""
import argparse
import csv
import io
import json
import math
from datetime import date
from pathlib import Path
from urllib.parse import urlsplit
from urllib.request import Request, urlopen
import subprocess
import uuid

ROOT = Path(__file__).resolve().parents[1]
MODES = ('rail', 'metro', 'tram', 'ferry', 'bus')


def validate(document):
    routes = document.get('routes')
    if document.get('schemaVersion') != 1 or not isinstance(routes, list) or not (1 <= len(routes) <= 500):
        raise ValueError('Invalid transit route collection')
    source_name = document.get('sourceName', 'Transport for NSW Public Timetable Reference')
    source_url = document.get('sourceUrl', 'https://transportnsw.info/routes')
    observed_on = document.get('observedOn', '2026-10-26')
    license_text = document.get('license', 'CC BY 4.0 Transport for NSW Open Data Reference')
    parsed_url = urlsplit(source_url)
    if parsed_url.scheme != 'https' or not parsed_url.hostname or parsed_url.username or parsed_url.password:
        raise ValueError('Invalid source URL')
    date.fromisoformat(observed_on)
    result = []
    seen = set()
    for row in routes:
        route_id = row.get('id', '')
        line = row.get('line', '')
        name = row.get('name', '')
        mode = row.get('mode', '')
        agency = row.get('agency', '')
        headway = row.get('headwayMinutes', 10)
        start_min = row.get('operatingStartMinute', 300)
        end_min = row.get('operatingEndMinute', 1439)
        bidirectional = bool(row.get('bidirectional', True))
        stops = row.get('stops', [])
        if route_id in seen or not isinstance(route_id, str) or not (1 <= len(route_id) <= 120):
            raise ValueError(f'Invalid or duplicate route ID: {route_id}')
        seen.add(route_id)
        if mode not in MODES:
            raise ValueError(f'Unsupported transit mode: {mode}')
        if not isinstance(line, str) or not (1 <= len(line) <= 30):
            raise ValueError('Invalid line code')
        if not isinstance(name, str) or not (1 <= len(name) <= 200):
            raise ValueError('Invalid route name')
        if not isinstance(agency, str) or not (1 <= len(agency) <= 160):
            raise ValueError('Invalid agency name')
        if not isinstance(headway, (int, float)) or not (1 <= headway <= 120):
            raise ValueError('Invalid headwayMinutes')
        if not isinstance(start_min, int) or not isinstance(end_min, int) or not (0 <= start_min < end_min <= 1440):
            raise ValueError('Invalid operating window')
        if not isinstance(stops, list) or not (2 <= len(stops) <= 80):
            raise ValueError('Each route requires 2..80 stops')
        normalized_stops = []
        prev_min = -1.0
        prev_meters = -1.0
        for stop in stops:
            stop_id = stop.get('id', '')
            stop_name = stop.get('name', '')
            lat = stop.get('lat')
            lng = stop.get('lng')
            platform = stop.get('platform', '')
            cum_min = stop.get('cumulativeMinutes')
            cum_meters = stop.get('cumulativeMeters')
            if not isinstance(stop_id, str) or not (1 <= len(stop_id) <= 120):
                raise ValueError('Invalid stop ID')
            if not isinstance(stop_name, str) or not (1 <= len(stop_name) <= 160):
                raise ValueError('Invalid stop name')
            if isinstance(lat, bool) or not isinstance(lat, (int, float)) or not math.isfinite(lat) or not (-90 <= lat <= 90):
                raise ValueError('Invalid stop latitude')
            if isinstance(lng, bool) or not isinstance(lng, (int, float)) or not math.isfinite(lng) or not (-180 <= lng <= 180):
                raise ValueError('Invalid stop longitude')
            if isinstance(cum_min, bool) or not isinstance(cum_min, (int, float)) or cum_min < prev_min:
                raise ValueError('cumulativeMinutes must be non-decreasing')
            if isinstance(cum_meters, bool) or not isinstance(cum_meters, (int, float)) or cum_meters < prev_meters:
                raise ValueError('cumulativeMeters must be non-decreasing')
            prev_min = float(cum_min)
            prev_meters = float(cum_meters)
            normalized_stops.append({
                'id': stop_id,
                'name': stop_name,
                'lat': float(lat),
                'lng': float(lng),
                'platform': str(platform) if platform else '',
                'cumulativeMinutes': float(cum_min),
                'cumulativeMeters': float(cum_meters),
            })
        result.append({
            'id': route_id,
            'line': line,
            'name': name,
            'mode': mode,
            'agency': agency,
            'bidirectional': bidirectional,
            'headwayMinutes': int(headway),
            'operatingStartMinute': start_min,
            'operatingEndMinute': end_min,
            'stops': normalized_stops,
            'sourceName': source_name,
            'sourceUrl': source_url,
            'observedOn': observed_on,
            'license': license_text,
        })
    return result


def read_env(path: Path) -> dict:
    env = {}
    for line in path.read_text().splitlines():
        key, sep, value = line.partition('=')
        if sep and not key.strip().startswith('#'):
            env[key.strip()] = value.strip().strip('"\'')
    return env


def post_json(url: str, body: dict, headers: dict = None) -> dict:
    req = Request(url, data=json.dumps(body).encode('utf-8'), headers={'Content-Type': 'application/json', **(headers or {})})
    with urlopen(req, timeout=60) as resp:
        return json.load(resp)


def sync_local(routes: list, source_env: Path, platform_env: Path) -> int:
    data = io.StringIO()
    writer = csv.writer(data, lineterminator='\n')
    for route in routes:
        writer.writerow([route['id'], json.dumps(route, ensure_ascii=False)])
    sql = '''BEGIN;
CREATE TABLE IF NOT EXISTS transit_route_records(id text PRIMARY KEY,payload text NOT NULL CHECK(jsonb_typeof(payload::jsonb)='object'));
CREATE OR REPLACE VIEW travel_transit_routes AS SELECT id,payload FROM transit_route_records;
GRANT SELECT ON travel_transit_routes TO travel_reader;
CREATE TEMP TABLE transit_batch(id text PRIMARY KEY,payload text) ON COMMIT DROP;
COPY transit_batch(id,payload) FROM STDIN WITH(FORMAT csv);
''' + data.getvalue() + '''\\.
DELETE FROM transit_route_records;
INSERT INTO transit_route_records SELECT * FROM transit_batch;
COMMIT;
'''
    subprocess.run([
        'docker', 'compose', '--env-file', str(source_env),
        '-f', str(ROOT / 'infra/zetaris/compose.source.yaml'),
        'exec', '-T', 'source-db', 'psql', '-U', 'travel_source_admin', '-d', 'travel_public', '-v', 'ON_ERROR_STOP=1'
    ], input=sql, text=True, capture_output=True, check=True)
    env = read_env(platform_env)
    auth = post_json('http://127.0.0.1:3000/api/auth/login', {
        'username': env['ZETARIS_ADMIN_EMAIL'],
        'password': env['ZETARIS_ADMIN_PASSWORD']
    })
    headers = {
        'Authorization': 'Bearer ' + auth['idToken'],
        'X-Org-ID': str(auth['orgId']),
    }

    def query(statement: str):
        res = post_json('http://127.0.0.1:8888/api/v1.0/sql-editor/sqls/run', {
            'queryId': str(uuid.uuid4()),
            'sql': statement,
            'source': 'SqlEditor',
            'limit': 2000
        }, {**headers, 'X-Request-ID': str(uuid.uuid4())})
        if not isinstance(res.get('data'), list):
            raise ValueError('Local Zetaris query failed')
        return res['data']

    tables = query('SHOW TABLES FROM sydneybuddy_travel')
    if not any(row[0].lower().endswith('travel_transit_routes') for row in tables):
        query('REGISTER DATASOURCE TABLE "travel_transit_routes" FROM sydneybuddy_travel')
    query('UNCACHE TABLE sydneybuddy_travel.travel_transit_routes')
    actual = [json.loads(row[0]) for row in query('SELECT payload FROM sydneybuddy_travel.travel_transit_routes')]
    if sorted(actual, key=lambda x: x['id']) != sorted(routes, key=lambda x: x['id']):
        raise ValueError('Local Zetaris transit payload verification failed')
    return len(actual)


def sync_cloud(routes: list, cloud_env: Path, folder: str = 'sydneybuddy-public-20261025') -> int:
    env = read_env(cloud_env)
    base_url = env.get('ZETARIS_BASE_URL', 'https://new-ui.a6c43f1d-2765-487d-89af-99b612b39de9.cloud.zetaris.com').rstrip('/')
    login_url = env.get('ZETARIS_LOGIN_URL', base_url + '/api/auth/login')
    auth = post_json(login_url, {
        'username': env['ZETARIS_ADMIN_EMAIL'],
        'password': env['ZETARIS_ADMIN_PASSWORD']
    })
    bearer = auth['idToken']
    org_id = str(auth['orgId'])
    # Cloud NDP's JSON reader only sees the first record of a multi-line file; a JSON array reads every record.
    jsonl_content = json.dumps([{'id': r['id'], 'payload': json.dumps(r, ensure_ascii=False)} for r in routes], ensure_ascii=False)
    boundary = '----SydneyBuddyTransitBoundary' + uuid.uuid4().hex
    body_parts = [
        f'--{boundary}\r\nContent-Disposition: form-data; name="path"\r\n\r\n/file-store/{folder}\r\n',
        f'--{boundary}\r\nContent-Disposition: form-data; name="files"; filename="travel_transit_routes.json"\r\nContent-Type: application/json\r\n\r\n{jsonl_content}\r\n',
        f'--{boundary}--\r\n'
    ]
    multipart_body = ''.join(body_parts).encode('utf-8')
    upload_req = Request(
        base_url + '/api/proxy/ndp-files/save',
        data=multipart_body,
        headers={
            'Authorization': 'Bearer ' + bearer,
            'X-Org-ID': org_id,
            'X-Request-ID': str(uuid.uuid4()),
            'Content-Type': f'multipart/form-data; boundary={boundary}'
        }
    )
    with urlopen(upload_req, timeout=60) as resp:
        if resp.status >= 400:
            raise ValueError('Cloud Zetaris NDP file upload failed')

    def query(statement: str):
        res = post_json(base_url + '/api/proxy/sql-editor/sqls/run-query', {
            'sql': statement,
            'limit': 2000
        }, {
            'Authorization': 'Bearer ' + bearer,
            'X-Org-ID': org_id,
            'X-Request-ID': str(uuid.uuid4())
        })
        if not isinstance(res.get('data'), list):
            raise ValueError('Cloud Zetaris query failed')
        return res['data']

    tables = query('SHOW TABLES FROM sydneybuddy_travel')
    if any(row[0].lower().endswith('travel_transit_routes') for row in tables):
        query('REFRESH TABLE sydneybuddy_travel.travel_transit_routes')
    else:
        query(f'CREATE LIGHTNING FILESTORE TABLE travel_transit_routes FROM sydneybuddy_travel FORMAT JSON OPTIONS(path "dffs://{folder}/travel_transit_routes.json", inferSchema "true")')
    actual = [json.loads(row[0]) for row in query('SELECT payload FROM sydneybuddy_travel.travel_transit_routes')]
    if sorted(actual, key=lambda x: x['id']) != sorted(routes, key=lambda x: x['id']):
        raise ValueError('Cloud Zetaris transit payload verification failed')
    return len(actual)


def main():
    cli = argparse.ArgumentParser(description=__doc__)
    cli.add_argument('--input', type=Path, default=ROOT / 'shared/sydney-transit-routes.json')
    cli.add_argument('--source-env', type=Path, default=ROOT / 'infra/zetaris/.env.local')
    cli.add_argument('--platform-env', type=Path)
    cli.add_argument('--cloud-env', type=Path)
    args = cli.parse_args()
    routes = validate(json.loads(args.input.read_text()))
    summary = {'status': 'verified', 'routes': len(routes)}
    if args.platform_env:
        summary['localZetarisCount'] = sync_local(routes, args.source_env, args.platform_env)
    if args.cloud_env:
        summary['cloudZetarisCount'] = sync_cloud(routes, args.cloud_env)
    print(json.dumps(summary))


if __name__ == '__main__':
    main()
