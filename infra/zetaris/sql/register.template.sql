-- TEMPLATE ONLY: substitute the private reader password in the Zetaris editor.
-- Do not commit the rendered SQL or print it in logs.
-- Inspect SHOW DATASOURCES first; do not recreate an existing source blindly.
CREATE DATASOURCE sydneybuddy_travel DESCRIBE BY "SydneyBuddy public travel test data" OPTIONS (
  jdbcdriver "org.postgresql.Driver",
  jdbcurl "jdbc:postgresql://host.docker.internal:55433/travel_public",
  username "travel_reader",
  password "REPLACE_PRIVATELY",
  fetchsize "1000"
);
REGISTER DATASOURCE TABLE "travel_poi" FROM sydneybuddy_travel;
REGISTER DATASOURCE TABLE "travel_catalog" FROM sydneybuddy_travel;
-- Weather is registered separately as a native live REST table, not a JDBC snapshot.
REGISTER DATASOURCE TABLE "travel_map_layers" FROM sydneybuddy_travel;
REGISTER DATASOURCE TABLE "travel_metadata" FROM sydneybuddy_travel;
