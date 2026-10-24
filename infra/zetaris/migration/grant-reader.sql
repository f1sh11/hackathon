-- PostgreSQL (not Zetaris). DBA must first provision a login role with a new AWS-only secret.
-- Pass the EXISTING role name using psql -v reader_role=travel_reader -f grant-reader.sql
\set ON_ERROR_STOP on
GRANT CONNECT ON DATABASE :"DBNAME" TO :"reader_role";
GRANT USAGE ON SCHEMA public TO :"reader_role";
GRANT SELECT ON travel_poi,travel_catalog,travel_map_layers,travel_metadata,travel_hidden_gems,travel_osm_places TO :"reader_role";
