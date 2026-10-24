# Local open place search

OpenStreetMap remains the map background. By default, its place search now uses a **real, local Overture Places snapshot**, not the public Overpass API. Apple Maps and Google Places keep their separate providers.

## Prepare the snapshot

From the repository root on macOS/Linux:

```sh
python3 -m venv .venv-open-poi
.venv-open-poi/bin/python -m pip install -r scripts/requirements-open-poi.txt
.venv-open-poi/bin/python scripts/import_overture_places.py
```

On Windows, replace `.venv-open-poi/bin/python` with `.venv-open-poi/Scripts/python.exe`.

The official Overture client downloads a bounded area from its public data releases. No API key is needed. The importer filters out records with existence confidence below 0.7, known closed places, invalid coordinates and irrelevant categories. It atomically replaces `infra/poi/data/places.json`; the backend reloads an updated snapshot without a restart. Downloads and normalization must complete before replacing the previous file. On macOS, the importer uses the system CA bundle if Python has no configured CA path; TLS verification stays enabled.

For an existing official download, normalization needs only Python's standard library:

```sh
python3 scripts/import_overture_places.py \
  --input /path/to/official-places.geojson --release 2026-10-21.0
```

The release must match the input file. Optional `--bbox=west,south,east,north` expands the downloaded area within the app's Sydney boundaries. The same bbox must be used when normalizing an existing download. `--minimum-confidence` controls the existence threshold; this does not verify coordinates, opening hours or accessibility.

## API and configuration

- Start the API as described in the main README. `IPoiProvider` defaults to `OverturePoiProvider`.
- `Poi__OvertureFile` can point to a different normalized snapshot. The default path is `infra/poi/data/places.json` under the repository root.
- Optional `Poi__OpenProvider=overpass` restores the legacy Overpass implementation. It is not a silent fallback.
- `POST /poi/search` retains the legacy `provider: "osm"` client value for the OpenStreetMap map. Its `source` and `metadata` explicitly identify the actual Overture data provider.
- Search uses the local normalized index, exact nearby-distance filtering, category, name/address/category words and a limit of 80. Empty queries browse the nearest matching places. No remote-service cooldown applies to local searches.
- Query circles outside the imported bbox produce a coverage error instead of claiming there are no places. Missing/invalid snapshots are also explicit errors; the server never generates substitute businesses.
- Overture IDs, source and contact fields survive the toolbox and demo schedule import. Fees, queue times, indoor/outdoor classification and opening hours remain unverified. The map and place-data attributions are displayed separately.

## Current development data

The local test snapshot downloaded on 2026-10-24 uses release `2026-10-21.0` and bbox `[151.18, -33.90, 151.24, -33.84]` (central Sydney). It contains 5,968 filtered places: 4,764 food/drink, 1,190 sights and 14 facilities. Data files are ignored by Git; run the importer on a fresh checkout or server. The 48 MB raw official GeoJSON is not bundled with the app or committed.

This is a regional data snapshot, not a worldwide search service or live business directory. Source records may contain wrong names, coordinates, classifications, duplicate businesses or stale contact details. Check the business's official source before travel. Reviews, photos, live availability, prices and verified opening hours are not provided by this integration.

## Sources and licenses

The index preserves the source release and each record's reported license. Overture Places uses permissive source licenses including CDLA-Permissive-2.0 and Apache-2.0; some records come from CC0 sources. Keep the Overture data attribution and the independent OpenStreetMap basemap attribution. See the official [download guide](https://docs.overturemaps.org/getting-data/overturemaps-py/), [Places guide](https://docs.overturemaps.org/guides/places/), [attribution and licensing](https://docs.overturemaps.org/attribution/), [CDLA Permissive 2.0](https://cdla.dev/permissive-2-0/) and [Apache 2.0](https://www.apache.org/licenses/LICENSE-2.0).

Tests: `python3 -m unittest discover -s scripts/tests -v`; backend `dotnet test server-dotnet/SydneyBuddy.Api.Tests`. Unit fixtures are synthetic; the local API and iOS manual checks used the downloaded real data.
