"""Validate and stage local open GTFS / OSM files for OTP. No credentials or network requests."""
from pathlib import Path
import argparse
import csv
import io
import shutil
import zipfile

REQUIRED = {'agency.txt', 'stops.txt', 'routes.txt', 'trips.txt', 'stop_times.txt'}

def validate_gtfs(path: Path):
    with zipfile.ZipFile(path) as archive:
        names = set(archive.namelist())
        missing = REQUIRED - names
        if missing or not ({'calendar.txt', 'calendar_dates.txt'} & names):
            raise ValueError('GTFS must contain root-level standard files and calendar/calendar_dates: ' + ', '.join(sorted(missing)))
        with archive.open('routes.txt') as stream:
            reader = csv.DictReader(io.TextIOWrapper(stream, encoding='utf-8-sig'))
            if not {'route_id', 'route_type'} <= set(reader.fieldnames or []):
                raise ValueError('routes.txt lacks route_id / route_type')
            count = sum(1 for _ in reader)
            if not count:
                raise ValueError('GTFS contains no routes')
        return count

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--gtfs', type=Path, action='append', required=True, help='Local official GTFS zip; repeat for multiple feeds')
    parser.add_argument('--osm', type=Path, required=True, help='Local matching .osm.pbf extract')
    parser.add_argument('--output', type=Path, default=Path(__file__).resolve().parents[1] / 'infra/transit/data')
    args = parser.parse_args()
    if not args.osm.is_file() or not args.osm.name.endswith('.osm.pbf') or args.osm.stat().st_size == 0:
        parser.error('Provide a non-empty .osm.pbf covering the same region as the GTFS.')
    try:
        counts = [validate_gtfs(path) for path in args.gtfs]
    except (ValueError, OSError, zipfile.BadZipFile) as error:
        parser.error(str(error))
    # Never overwrite a previous graph/feed: graph rebuild is an explicit operator action.
    targets = [args.output / 'region.osm.pbf'] + [args.output / f'feed-{i+1}-gtfs.zip' for i in range(len(args.gtfs))]
    if any(path.exists() for path in targets):
        parser.error('Staging files already exist; use a new --output directory to preserve the current graph.')
    args.output.mkdir(parents=True, exist_ok=True)
    for source, target in zip([args.osm] + args.gtfs, targets):
        shutil.copyfile(source, target)
    print(f'Staged {len(counts)} GTFS feeds / {sum(counts)} routes and one OSM extract. Validate service dates before building OTP.')
    print('Build with: docker compose -f infra/transit/compose.yaml run --rm otp --build --save')
    print('Serve with: docker compose -f infra/transit/compose.yaml up -d')

if __name__ == '__main__':
    main()
