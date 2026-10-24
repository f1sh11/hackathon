-- Native public REST tables. Inspect SHOW TABLES FROM sydneybuddy_live first; run only missing CREATE statements.
-- Requires the sydneybuddy_live database from weather.sql. No API keys, no CACHE commands.
CREATE LIGHTNING REST TABLE traffic_nsw FROM sydneybuddy_live REQUEST(
 endpoint "https://data.livetraffic.com/traffic/hazards/incident-open.json", method "get", response_type "json", http_encoding "URLENCODED"
) HEADER () BODY ();
CREATE LIGHTNING REST TABLE fees_sydney FROM sydneybuddy_live REQUEST(
 endpoint "https://overpass-api.de/api/interpreter?data=%5Bout%3Ajson%5D%5Btimeout%3A15%5D%3Bnwr%5B%22charge%22%5D%28-33.90%2C151.18%2C-33.82%2C151.25%29%3Bout+center+tags+500%3B", method "get", response_type "json", http_encoding "URLENCODED"
) HEADER () BODY ();
