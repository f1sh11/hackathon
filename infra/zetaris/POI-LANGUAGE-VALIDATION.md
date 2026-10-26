# POI language validation — 2026-10-27

English presentation now uses optional `nameEn` and `descriptionEn` source fields. Canonical names, place IDs, category codes, source quotations and existing trip data remain unchanged. Legacy saved catalog/OSM names use the UI translation catalog.

## Root causes and fixes

- OSM normalization selected Chinese names/descriptions and discarded original English variants. The importer and live provider now retain both fields; 12,929 cached source records were enriched without deleting the other 581 records.
- Discovery lists, detail sheets, toolbox entries and map annotations rendered raw names. Both mobile clients now select English presentation fields. iOS annotation refresh keys include language; trip stop titles also use the catalog.
- Search used only canonical/searchText fields. Unified search now includes English names and descriptions, so English and Chinese queries resolve the same IDs.
- Toolbox import retains bounded English fields (200/3,000 characters). Translation never replaces the canonical stored name.
- All 55 curated catalog entries have English names and descriptions. Source-provided OSM names also supply catalog fallbacks for older saved POIs.

## Verified

- ASP.NET Core complete suite: 406 passed.
- Android unit suite: 37 passed, including two POI language tests.
- iOS build and three simulator language regression tests passed.
- OSM importer suite: five passed, including bilingual source retention.
- Real local and cloud Zetaris: 13,510 OSM records and 55 catalog records matched the enriched payloads individually after synchronization.
- Isolated ASP.NET Core integration: `Opera` and `悉尼歌剧院` both returned catalog ID `opera`, English name `Sydney Opera House` and its English description, through each Zetaris source. Required iOS decoding fields were present.
- Generated iOS localization resources validated against the 3,750-entry shared catalog. Added entries retain Traditional and Simplified Chinese scripts.
- Added source-English display fallbacks for bilingual Overture business names; proper names without an English source remain unchanged.

## Remaining deployment check

Oracle HTTPS and SSH were unavailable during verification. The Mac was locked, so Oracle console recovery and visual UI inspection could not proceed. These checks do not establish that the Oracle deployment is healthy or updated. Updated public backend packaging is prepared separately; no secrets are included.

Apple base-map labels follow MapKit/system map behavior. Author-provided reviews and quotations keep their original language; unknown proper names are never fabricated as translations.
