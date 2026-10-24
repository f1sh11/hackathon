-- Dedicated public community collection. Does not touch user trips or existing public records.
\set ON_ERROR_STOP on
BEGIN;
CREATE TABLE IF NOT EXISTS hidden_gem_records (
 id text PRIMARY KEY CHECK (id ~ '^gem:[a-f0-9]{24}$'),name text NOT NULL,
 lat double precision NOT NULL CHECK(lat BETWEEN -34.2 AND -33.5),
 lng double precision NOT NULL CHECK(lng BETWEEN 150.5 AND 151.5),
 payload text NOT NULL CHECK(jsonb_typeof(payload::jsonb)='object')
);
CREATE TEMP TABLE incoming_gems (LIKE hidden_gem_records INCLUDING ALL) ON COMMIT DROP;
\copy incoming_gems FROM '/imports/hidden-gems.csv' WITH (FORMAT csv,HEADER true)
DO $$ BEGIN IF NOT EXISTS(SELECT 1 FROM incoming_gems) THEN RAISE EXCEPTION 'Refuse empty community import'; END IF; END $$;
DELETE FROM hidden_gem_records;
INSERT INTO hidden_gem_records SELECT * FROM incoming_gems;
CREATE OR REPLACE VIEW travel_hidden_gems AS SELECT id,name,lat,lng,payload FROM hidden_gem_records;
GRANT SELECT ON travel_hidden_gems TO travel_reader;
COMMIT;
SELECT count(*) AS imported_gems FROM travel_hidden_gems;
