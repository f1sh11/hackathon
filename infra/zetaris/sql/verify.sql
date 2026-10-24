SELECT 1 AS one;
SHOW DATASOURCES;
SELECT COUNT(*) AS poi_count FROM sydneybuddy_travel.travel_poi;
SELECT id,name,category,lat,lng FROM sydneybuddy_travel.travel_poi WHERE category = 'food' LIMIT 10;
-- Live query: never CACHE this REST table.
SELECT to_json(struct(*)) AS payload FROM sydneybuddy_live.weather_sydney;
SELECT id,name,lat,lng FROM sydneybuddy_travel.travel_hidden_gems;
SELECT id,name,source FROM sydneybuddy_travel.travel_map_layers;
