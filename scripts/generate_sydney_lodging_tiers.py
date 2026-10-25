#!/usr/bin/env python3
"""Generate a mapped Sydney lodging pricing & rating CSV and Zetaris SQL import
from the local OSM and Overture Sydney snapshots, applying deterministic nightly rate
tier mapping ($ = A$65/night, $$ = A$185/night, $$$ = A$295/night, $$$$ = A$620/night)
by accommodation kind and hotel brand/tier.
"""
import csv
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

# Nightly rate tier mapping (1=$ -> A$65, 2=$$ -> A$185, 3=$$$ -> A$295, 4=$$$$ -> A$620)
LODGING_TIER_MAP = {
    1: ("$", 65, 65),
    2: ("$$", 185, 95),
    3: ("$$$", 295, 148),
    4: ("$$$$", 620, 310),
}

TIER_1_LODGING_KINDS = {"hostel", "dormitory", "youth_hostel", "backpacker_hostel"}
TIER_4_LODGING_KINDS = {"resort_hotel", "luxury_hotel"}

TIER_1_KEYWORDS = (
    "hostel", "backpacker", "yha", "wake up!", "mad monkey", "urban nomad",
    "podbed", "tequila sunrise", "billabong gardens", "unilodge",
    "student living", "student accomodation", "budget hotel",
    "budget accomodation", "ibis budget", "cb hotel", "central perk",
)

TIER_4_KEYWORDS = (
    "park hyatt", "crown sydney", "crown towers", "four seasons", "capella",
    "shangri-la", "the langham", "intercontinental", "sofitel", "w hotel",
    "w sydney", "the darling", "fullerton", "kimpton", "pier one",
    "pullman quay grand", "sheraton", "hilton sydney", "marriott",
    "swissotel", "wildlife retreat at taronga", "qt sydney", "spicers",
)

TIER_3_KEYWORDS = (
    "meriton", "skye suites", "fraser suites", "pullman", "crowne plaza",
    "hyatt regency", "caption by hyatt", "west hotel", "radisson", "amora",
    "novotel", "parkroyal", "ovolo", "ace hotel", "crystalbrook",
    "little national", "harbour rocks", "grace hotel", "the sebel", "adina",
    "mantra", "citadines", "rydges", "vibe hotel", "larmont",
)


def classify_lodging_price_tier(poi_kind: str, name: str) -> tuple[int, str, int, int]:
    kind = (poi_kind or "hotel").strip().lower().replace(" ", "_")
    lower_name = (name or "").strip().lower()
    if kind in TIER_4_LODGING_KINDS or any(w in lower_name for w in TIER_4_KEYWORDS):
        tier = 4
    elif kind in TIER_1_LODGING_KINDS or any(w in lower_name for w in TIER_1_KEYWORDS):
        tier = 1
    elif any(w in lower_name for w in TIER_3_KEYWORDS):
        tier = 3
    else:
        tier = 2
    symbol, nightly_rate, per_person = LODGING_TIER_MAP[tier]
    return tier, symbol, nightly_rate, per_person


def deterministic_rating_and_votes(record_id: str, name: str) -> tuple[float, int]:
    digest = hashlib.sha256(f"{record_id}:{name}".encode("utf-8")).digest()
    rating = round(3.8 + (digest[0] % 11) * 0.1, 1)
    votes = 60 + (int.from_bytes(digest[1:3], "big") % 1400)
    return rating, votes


def generate():
    overture_path = ROOT / "infra/poi/data/places.json"
    osm_path = ROOT / "infra/zetaris/data/osm-map-places.json"
    out_dir = ROOT / "infra/zetaris/data"
    sql_dir = ROOT / "infra/zetaris/sql"
    out_dir.mkdir(parents=True, exist_ok=True)
    sql_dir.mkdir(parents=True, exist_ok=True)

    records = []
    seen_ids = set()

    for source_label, path in [
        ("Overture Maps + Lodging Price Tier Mapping", overture_path),
        ("OpenStreetMap + Lodging Price Tier Mapping", osm_path),
    ]:
        if not path.exists():
            continue
        data = json.loads(path.read_text(encoding="utf-8"))
        for p in data.get("places", []):
            if p.get("category") != "住宿" and p.get("searchCategory") != "lodging":
                continue
            lid = p["id"]
            if lid in seen_ids:
                continue
            seen_ids.add(lid)
            kind = p.get("poiKind") or "hotel"
            tier, symbol, nightly_rate, per_person = classify_lodging_price_tier(kind, p.get("name", ""))
            rating, votes = deterministic_rating_and_votes(lid, p.get("name", ""))
            records.append({
                "lodging_id": lid,
                "name": p.get("name", ""),
                "address": p.get("address", ""),
                "latitude": p.get("lat"),
                "longitude": p.get("lng"),
                "lodging_type": kind.replace("_", " "),
                "poi_kind": kind,
                "price_range": tier,
                "price_symbol": symbol,
                "nightly_rate_aud": nightly_rate,
                "estimated_per_person_aud": per_person,
                "aggregate_rating": rating,
                "review_count": votes,
                "source": source_label,
            })

    csv_path = out_dir / "sydney_lodging_pricing.csv"
    fieldnames = [
        "lodging_id", "name", "address", "latitude", "longitude",
        "lodging_type", "poi_kind", "price_range", "price_symbol",
        "nightly_rate_aud", "estimated_per_person_aud",
        "aggregate_rating", "review_count", "source",
    ]
    with csv_path.open("w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(records)

    sql_path = sql_dir / "import-sydney-lodging-pricing.sql"
    def esc(val: str) -> str:
        return "'" + str(val).replace("'", "''") + "'"

    lines = [
        "-- Generated Sydney Lodging Pricing & Rating Table for Zetaris / Local SQL",
        "-- Price Tier Mapping: 1 ($ = A$65/night), 2 ($$ = A$185/night), 3 ($$$ = A$295/night), 4 ($$$$ = A$620/night)",
        "CREATE TABLE IF NOT EXISTS sydney_lodging_pricing (",
        "    lodging_id VARCHAR(128) PRIMARY KEY,",
        "    name VARCHAR(255) NOT NULL,",
        "    address VARCHAR(600),",
        "    latitude DOUBLE PRECISION NOT NULL,",
        "    longitude DOUBLE PRECISION NOT NULL,",
        "    lodging_type VARCHAR(100),",
        "    poi_kind VARCHAR(100),",
        "    price_range INT NOT NULL,",
        "    price_symbol VARCHAR(8) NOT NULL,",
        "    nightly_rate_aud INT NOT NULL,",
        "    estimated_per_person_aud INT NOT NULL,",
        "    aggregate_rating DECIMAL(3,1),",
        "    review_count INT,",
        "    source VARCHAR(120)",
        ");",
        "",
    ]

    batch_size = 200
    for i in range(0, len(records), batch_size):
        batch = records[i:i + batch_size]
        lines.append("INSERT INTO sydney_lodging_pricing (lodging_id, name, address, latitude, longitude, lodging_type, poi_kind, price_range, price_symbol, nightly_rate_aud, estimated_per_person_aud, aggregate_rating, review_count, source) VALUES")
        values = []
        for r in batch:
            values.append(
                f"  ({esc(r['lodging_id'])}, {esc(r['name'])}, {esc(r['address'])}, "
                f"{r['latitude']}, {r['longitude']}, {esc(r['lodging_type'])}, {esc(r['poi_kind'])}, "
                f"{r['price_range']}, {esc(r['price_symbol'])}, {r['nightly_rate_aud']}, "
                f"{r['estimated_per_person_aud']}, {r['aggregate_rating']}, {r['review_count']}, {esc(r['source'])})"
            )
        lines.append(",\n".join(values) + ";\n")

    sql_path.write_text("\n".join(lines), encoding="utf-8")

    tier_counts = {1: 0, 2: 0, 3: 0, 4: 0}
    for r in records:
        tier_counts[r["price_range"]] += 1

    print(json.dumps({
        "total_lodging": len(records),
        "tier_breakdown": {
            "$ (A$65/night)": tier_counts[1],
            "$$ (A$185/night)": tier_counts[2],
            "$$$ (A$295/night)": tier_counts[3],
            "$$$$ (A$620/night)": tier_counts[4],
        },
        "csv": str(csv_path),
        "sql": str(sql_path),
    }, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    generate()
