#!/usr/bin/env python3
"""Restore the verified bundle into an explicitly named NEW dedicated PostgreSQL database."""
import argparse
import json
import os
import re
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parent

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--database', required=True, help='Expected existing EMPTY dedicated database name')
    parser.add_argument('--execute', action='store_true', help='Run the import; otherwise only verifies the bundle')
    args = parser.parse_args()
    if not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]{0,62}", args.database):
        raise ValueError("Use a simple dedicated PostgreSQL database name")
    subprocess.run([sys.executable, str(ROOT / 'verify_bundle.py'), '--verify', str(ROOT)], check=True)
    manifest = json.loads((ROOT / 'manifest.json').read_text())
    if not args.execute:
        print('Bundle verified. No database changes. Configure PGSERVICE/PGPASSFILE then add --execute.')
        return
    # libpq resolves host/auth from PGSERVICE / environment / pgpass. Never put passwords on CLI.
    env = dict(os.environ, PGDATABASE=args.database)
    command = ['psql', '-X', '-v', 'ON_ERROR_STOP=1', '--dbname', args.database]
    connected = subprocess.check_output(command + ['-A', '-t', '-c', 'SELECT current_database()'], env=env, text=True).strip()
    if connected != args.database:
        raise ValueError('Connected database differs from the expected dedicated database')
    occupied = subprocess.check_output(command + ['-A', '-t', '-c',
        "SELECT count(*) FROM pg_class c JOIN pg_namespace n ON c.relnamespace=n.oid WHERE n.nspname='public' AND c.relkind IN ('r','v','m','S','f','p')"], env=env, text=True).strip()
    if occupied != '0':
        raise ValueError('Dedicated public database must be empty')
    # SQL has CREATE TABLE without IF NOT EXISTS and no DROP/DELETE/TRUNCATE.
    # Existing tables cause the entire import to roll back.
    # Counts are checked INSIDE the same transaction before committing.
    counts = manifest['counts']
    checks = []
    for name in ('poi', 'catalog', 'map_layers', 'metadata', 'hidden_gems', 'osm_places'):
        view = {'poi': 'travel_poi', 'catalog': 'travel_catalog', 'map_layers': 'travel_map_layers',
                'metadata': 'travel_metadata', 'hidden_gems': 'travel_hidden_gems', 'osm_places': 'travel_osm_places'}[name]
        checks.append(f"IF (SELECT count(*) FROM {view}) <> {int(counts[name])} THEN RAISE EXCEPTION 'Row count mismatch: {view}'; END IF;")
    sql = (ROOT / 'restore.sql').read_text() + '\nDO $$ BEGIN\n' + '\n'.join(checks) + '\nEND $$;\n'
    subprocess.run(command + ['--single-transaction', '-f', '-'], input=sql, text=True, cwd=ROOT, env=env, check=True)
    print('Public source import committed. Next: grant reader access and register views in AWS Zetaris.')

if __name__ == '__main__':
    try:
        main()
    except (OSError, ValueError, subprocess.CalledProcessError) as error:
        print('Restore failed: ' + type(error).__name__, file=sys.stderr)
        sys.exit(1)
