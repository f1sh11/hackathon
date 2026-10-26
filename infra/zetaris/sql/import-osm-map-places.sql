\set ON_ERROR_STOP on
BEGIN;
CREATE TABLE IF NOT EXISTS osm_map_records (
 id text PRIMARY KEY CHECK (id ~ '^osm:(node|way|relation):[0-9]+$'),name text NOT NULL,
 lat double precision NOT NULL CHECK(lat BETWEEN -37.5 AND -28.0),
 lng double precision NOT NULL CHECK(lng BETWEEN 141.0 AND 160.0),
 payload text NOT NULL CHECK(jsonb_typeof(payload::jsonb)='object')
);
ALTER TABLE osm_map_records DROP CONSTRAINT IF EXISTS osm_map_records_lat_check;
ALTER TABLE osm_map_records DROP CONSTRAINT IF EXISTS osm_map_records_lng_check;
ALTER TABLE osm_map_records ADD CONSTRAINT osm_map_records_lat_check CHECK (lat BETWEEN -37.5 AND -28.0);
ALTER TABLE osm_map_records ADD CONSTRAINT osm_map_records_lng_check CHECK (lng BETWEEN 141.0 AND 160.0);
CREATE TEMP TABLE incoming_osm (LIKE osm_map_records INCLUDING ALL) ON COMMIT DROP;
\copy incoming_osm FROM '/imports/osm-map-places.csv' WITH (FORMAT csv,HEADER true)
DO $$ BEGIN IF NOT EXISTS(SELECT 1 FROM incoming_osm) THEN RAISE EXCEPTION 'Refuse empty OSM import'; END IF; END $$;
DELETE FROM osm_map_records;
INSERT INTO osm_map_records SELECT * FROM incoming_osm;
CREATE OR REPLACE VIEW travel_osm_places AS SELECT id,name,lat,lng,payload FROM osm_map_records;
GRANT SELECT ON travel_osm_places TO travel_reader;
COMMIT;
SELECT count(*) AS imported_osm FROM travel_osm_places;
