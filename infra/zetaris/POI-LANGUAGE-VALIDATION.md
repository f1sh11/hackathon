# POI language validation — 2026-10-27

English presentation now uses optional `nameEn` and `descriptionEn` source fields. Canonical names, place IDs, category codes, source quotations and existing trip data remain unchanged. Legacy saved catalog/OSM names use the UI translation catalog.

## Root causes and fixes

- OSM normalization selected Chinese names/descriptions and discarded original English variants. The importer and live provider now retain both fields; 12,929 cached source records were enriched without deleting the other 581 records.
- Discovery lists, detail sheets, toolbox entries and map annotations rendered raw names. Both mobile clients now select English presentation fields. iOS annotation refresh keys include language; trip stop titles also use the catalog.
- Search used only canonical/searchText fields. Unified search now includes English names and descriptions, so English and Chinese queries resolve the same IDs.
- Toolbox import retains bounded English fields (200/3,000 characters). Translation never replaces the canonical stored name.
- All 55 curated catalog entries have English names and descriptions. Source-provided OSM names also supply catalog fallbacks for older saved POIs.

## Verified

- ASP.NET Core complete suite: 407 passed after adding the bounded-cache regression test.
- Android unit suite: 37 passed, including two POI language tests.
- iOS build and three simulator language regression tests passed; the reference-charge marker interpolation test also passed after visual inspection found the remaining Chinese price tooltip.
- OSM importer suite: five passed, including bilingual source retention.
- Real local and cloud Zetaris: 13,510 OSM records and 55 catalog records matched the enriched payloads individually after synchronization.
- Isolated ASP.NET Core integration: `Opera` and `悉尼歌剧院` both returned catalog ID `opera`, English name `Sydney Opera House` and its English description, through each Zetaris source. Required iOS decoding fields were present.
- Generated iOS localization resources validated against the 3,752-entry shared catalog. Added entries retain Traditional and Simplified Chinese scripts.
- Added source-English display fallbacks for bilingual Overture business names; proper names without an English source remain unchanged.

## Oracle recovery and deployment

- The existing Sydney Always Free VM was recovered with the previously authorized normal reboot. No instance or network resources were added.
- Existing memory reservation remains disabled (`crashkernel=0`); 945 MiB RAM and 2,545 MiB swap were available after reboot. The root filesystem had about 20 GiB free.
- Pre-reboot monitoring showed memory rising from about 20% to about 90%, then missing samples. No persistent previous-boot journal was available, so the exact cause of the outage is unconfirmed.
- Discovered and fixed a separate memory risk: every dragged viewport could retain a distinct snapshot for 15 seconds. The cache now keeps only the latest snapshot per source/configuration and checks the viewport before reuse. A regression test covers 30 successive viewports and returning to an earlier viewport.
- Saved rollback image `localhost/sydneybuddy-api:before-regional-poi` and deployed the updated amd64 image. Verified the running Oracle DLL contains the new `travel-public-v8` cache key and excludes the old key.
- Oracle API, PostgreSQL and HTTPS services are active. Health reports managed source `zetaris-cloud`, TfNSW configured and paid services disabled.
- Public HTTPS rejects unauthenticated requests. Authenticated searches returned 25 Opera matches, 43 Katoomba hotels and 41 cafés, all marked `zetaris-cloud` (about 3–6 seconds in this run).
- iOS simulator connected to the Oracle HTTPS URL with its existing saved access code. The App automatically selected Cloud Zetaris (Sydney), with no Zetaris credentials entered into the App, and discovery results rendered successfully.
- After queries, the API used about 231 MiB within its 320 MiB container limit; the VM reported about 335 MiB available. This is a short functional check, not a sustained multi-user load test.

Apple base-map labels follow MapKit/system map behavior. Author-provided reviews and quotations keep their original language; unknown proper names are never fabricated as translations.
