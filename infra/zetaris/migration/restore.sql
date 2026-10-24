-- Fresh dedicated public database ONLY. Executed inside one transaction by restore_source.py.
\set ON_ERROR_STOP on
SET LOCAL search_path TO public, pg_catalog;
CREATE TABLE travel_records (
    dataset text NOT NULL CHECK (dataset IN ('poi','catalog','map_layers','metadata')),
    record_id text NOT NULL,
    name text NOT NULL,
    category text NOT NULL,
    lat double precision CHECK (lat BETWEEN -90 AND 90),
    lng double precision CHECK (lng BETWEEN -180 AND 180),
    source text NOT NULL,
    observed_at timestamptz NOT NULL,
    payload text NOT NULL CHECK (jsonb_typeof(payload::jsonb) = 'object'),
    PRIMARY KEY (dataset,record_id)
);
CREATE INDEX travel_search ON travel_records (dataset,category,lat,lng);
CREATE VIEW travel_poi AS SELECT record_id AS id,name,category,lat,lng,source,observed_at,payload FROM travel_records WHERE dataset='poi';
CREATE VIEW travel_catalog AS SELECT record_id AS id,name,category,lat,lng,source,observed_at,payload FROM travel_records WHERE dataset='catalog';
CREATE VIEW travel_map_layers AS SELECT record_id AS id,name,source,payload FROM travel_records WHERE dataset='map_layers';
CREATE VIEW travel_metadata AS SELECT record_id AS id,source,observed_at,payload FROM travel_records WHERE dataset='metadata';
CREATE TABLE hidden_gem_records (
 id text PRIMARY KEY, name text NOT NULL,
 lat double precision NOT NULL CHECK(lat BETWEEN -90 AND 90),
 lng double precision NOT NULL CHECK(lng BETWEEN -180 AND 180),
 payload text NOT NULL CHECK(jsonb_typeof(payload::jsonb)='object')
);
CREATE VIEW travel_hidden_gems AS SELECT id,name,lat,lng,payload FROM hidden_gem_records;
CREATE TABLE osm_map_records (
 id text PRIMARY KEY, name text NOT NULL,
 lat double precision NOT NULL CHECK(lat BETWEEN -90 AND 90),
 lng double precision NOT NULL CHECK(lng BETWEEN -180 AND 180),
 payload text NOT NULL CHECK(jsonb_typeof(payload::jsonb)='object')
);
CREATE VIEW travel_osm_places AS SELECT id,name,lat,lng,payload FROM osm_map_records;
\copy travel_records FROM 'data/travel-records.csv' WITH (FORMAT csv,HEADER true)
\copy hidden_gem_records FROM 'data/hidden-gems.csv' WITH (FORMAT csv,HEADER true)
\copy osm_map_records FROM 'data/osm-map-places.csv' WITH (FORMAT csv,HEADER true)
