CREATE TABLE IF NOT EXISTS schema_versions (
    version integer PRIMARY KEY,
    applied_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS profiles (
    id uuid PRIMARY KEY,
    singleton boolean NOT NULL DEFAULT true UNIQUE CHECK (singleton),
    answers jsonb NOT NULL,
    values jsonb NOT NULL,
    character text NOT NULL,
    created_at timestamptz NOT NULL
);
CREATE TABLE IF NOT EXISTS trips (
    id uuid PRIMARY KEY,
    profile_id uuid NOT NULL REFERENCES profiles(id),
    title text NOT NULL,
    request jsonb NOT NULL,
    status text NOT NULL CHECK (status IN ('completed','infeasible','failed')),
    idempotency_key uuid NOT NULL UNIQUE,
    created_at timestamptz NOT NULL
);
CREATE TABLE IF NOT EXISTS trip_versions (
    trip_id uuid NOT NULL REFERENCES trips(id),
    version integer NOT NULL CHECK (version > 0),
    easier boolean NOT NULL,
    plan jsonb,
    conflicts jsonb NOT NULL,
    idempotency_key uuid NOT NULL UNIQUE,
    created_at timestamptz NOT NULL,
    PRIMARY KEY (trip_id,version)
);
CREATE INDEX IF NOT EXISTS ix_trips_created ON trips(created_at DESC);
-- Media bytes belong to a local directory/object store; only references are stored here.
-- No upload endpoint in this MVP. This table reserves a clean extension point.
CREATE TABLE IF NOT EXISTS media_assets (
    id uuid PRIMARY KEY,
    trip_id uuid REFERENCES trips(id),
    storage_key text NOT NULL UNIQUE,
    content_type text NOT NULL,
    byte_size bigint NOT NULL CHECK (byte_size >= 0),
    created_at timestamptz NOT NULL DEFAULT now()
);
INSERT INTO schema_versions(version) VALUES (1) ON CONFLICT DO NOTHING;
