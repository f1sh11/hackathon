\set ON_ERROR_STOP on
CREATE TABLE travel_records (
    dataset text NOT NULL CHECK (dataset IN ('poi','catalog','weather','map_layers','metadata')),
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
CREATE VIEW travel_weather AS SELECT record_id AS forecast_date,lat,lng,source,observed_at,payload FROM travel_records WHERE dataset='weather';
CREATE VIEW travel_map_layers AS SELECT record_id AS id,name,source,payload FROM travel_records WHERE dataset='map_layers';
CREATE VIEW travel_metadata AS SELECT record_id AS id,source,observed_at,payload FROM travel_records WHERE dataset='metadata';
\getenv reader_password TRAVEL_READER_PASSWORD
SELECT format('CREATE ROLE travel_reader LOGIN PASSWORD %L', :'reader_password') \gexec
GRANT CONNECT ON DATABASE travel_public TO travel_reader;
GRANT USAGE ON SCHEMA public TO travel_reader;
GRANT SELECT ON travel_poi,travel_catalog,travel_weather,travel_map_layers,travel_metadata TO travel_reader;
