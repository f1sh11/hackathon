-- Run only after SHOW LIGHTNING DATABASES / SHOW TABLES confirms these names are absent.
-- If already present, inspect its endpoint; do not blindly recreate or overwrite it.
CREATE LIGHTNING DATABASE sydneybuddy_live DESCRIBE BY "SydneyBuddy live public APIs";
CREATE LIGHTNING REST TABLE weather_sydney FROM sydneybuddy_live REQUEST(
 endpoint "https://api.open-meteo.com/v1/forecast?latitude=-33.8688&longitude=151.2093&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max&timezone=Australia%2FSydney&forecast_days=7",
 method "get", response_type "json", http_encoding "URLENCODED"
) HEADER () BODY ();
-- Do not CACHE this table. Each SELECT must invoke the live source.
