BEGIN;
CREATE TABLE IF NOT EXISTS public.sydney_restaurants_pricing (
    restaurant_id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    address TEXT,
    latitude DOUBLE PRECISION NOT NULL,
    longitude DOUBLE PRECISION NOT NULL,
    cuisine TEXT NOT NULL,
    poi_kind TEXT NOT NULL,
    price_range INT NOT NULL,
    price_symbol TEXT NOT NULL,
    avg_cost_for_two_aud NUMERIC(10,2) NOT NULL,
    estimated_per_person_aud NUMERIC(10,2) NOT NULL,
    aggregate_rating NUMERIC(3,2),
    review_count INT,
    source TEXT NOT NULL
);
CREATE OR REPLACE VIEW public.travel_sydney_restaurants_pricing AS
SELECT * FROM public.sydney_restaurants_pricing;
GRANT SELECT ON public.sydney_restaurants_pricing TO travel_reader;
GRANT SELECT ON public.travel_sydney_restaurants_pricing TO travel_reader;
COMMIT;
