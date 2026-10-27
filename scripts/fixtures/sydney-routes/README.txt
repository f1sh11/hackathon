Sydney three-day Demo route fixtures

The numbered JSON files contain public provider responses and their exact request,
URL and fetch time. They contain no API keys. They are build inputs; the iOS app
uses the bundled sydney-mixed-transport.json and makes no routing requests for Demo.

Providers:
- Road walking, driving and cycling: FOSSGIS public OSRM, OpenStreetMap data.
  https://routing.openstreetmap.de/about.html
  Requests are sequential, at most one per 1.3 seconds, and cached permanently.
- Rail, bus and ferry: existing local /journey/plan adapter, Transport for NSW.
  These are cached route shapes, NOT live services, ticket prices or bookings.
- grand-drive-way.json: points sampled from OSM Grand Drive way 2664870, which
  is tagged bicycle=yes and oneway=yes. They constrain the cycling loop to the park.
- Hotel access is the Pitt Street public approach beside OSM building 335687066
  (30 Pitt Street), not the original misplaced coordinate or an interior doorway.

Rules:
- Full road and transit geometry must originate in the provider responses.
- Route-to-marker and step handover gaps must not exceed 3 metres.
- A vehicle centreline can be a few metres from the pavement/wharf. Only for
  boarding/alighting, an explicitly estimated walking link is allowed, <=20m.
  These short links are labelled in agency, and independently length-checked.
  They never replace a missing street route or an unmodelled station transfer.
- Same-location hotel check-out is zero distance, not a fabricated journey.
- Schedule times are illustrative and shifted to the requested Sydney date.
- RouteLeg stores timing/mode metadata; geometry is kept once in
  estimatedTransportPlan to fit the existing 250,000-byte backend request limit.
- Missing caches or disconnected geometry fail the build; no straight-line fallback.
- Old saved hand-drawn Demos are blocked. The app offers a corrected preview and
  saves only on confirmation; it does not silently replace saved user itineraries.

Offline rebuild and validation:
  python3 scripts/build_three_day_demo.py
  python3 scripts/test_demo_route_connections.py

To populate new queries intentionally, with the existing local TfNSW backend:
  python3 scripts/build_three_day_demo.py --fetch-routes

Attribution is visible on the route map and in transport details, including the
OpenStreetMap copyright and fix-the-map links. Photos retain separate CC credits.
