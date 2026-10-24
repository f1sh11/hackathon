-- Replace only the dedicated public-data staging table, atomically.
\set ON_ERROR_STOP on
BEGIN;
CREATE TEMP TABLE incoming (LIKE travel_records INCLUDING ALL) ON COMMIT DROP;
\copy incoming FROM '/imports/travel-records.csv' WITH (FORMAT csv,HEADER true)
DO $$ BEGIN IF NOT EXISTS(SELECT 1 FROM incoming WHERE dataset='poi') THEN RAISE EXCEPTION 'No POIs: refuse empty import'; END IF; END $$;
DELETE FROM travel_records;
INSERT INTO travel_records SELECT * FROM incoming;
COMMIT;
SELECT dataset,count(*) FROM travel_records GROUP BY dataset ORDER BY dataset;
