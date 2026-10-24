-- AWS Zetaris SQL Workspace; compare the six counts against manifest.json.
SHOW DATASOURCES;
SHOW TABLES FROM sydneybuddy_travel;
SELECT count(*) FROM sydneybuddy_travel.travel_poi;
SELECT count(*) FROM sydneybuddy_travel.travel_catalog;
SELECT count(*) FROM sydneybuddy_travel.travel_map_layers;
SELECT count(*) FROM sydneybuddy_travel.travel_metadata;
SELECT count(*) FROM sydneybuddy_travel.travel_hidden_gems;
SELECT count(*) FROM sydneybuddy_travel.travel_osm_places;
SELECT payload FROM sydneybuddy_travel.travel_hidden_gems LIMIT 1;
SELECT to_json(struct(*)) AS payload FROM sydneybuddy_live.weather_sydney;
-- Run weather SELECT a second time. Use engine/source request logs to prove two HTTP requests;
-- equal forecast values alone neither prove nor disprove a cache.

-- Optional public insights, after registering insights.sql:
SELECT to_json(struct(*)) AS payload FROM sydneybuddy_live.traffic_nsw;
SELECT to_json(struct(*)) AS payload FROM sydneybuddy_live.fees_sydney;
