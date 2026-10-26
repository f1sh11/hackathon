#!/usr/bin/env python3
"""Export verified Wikimedia Commons attraction photos from hidden-gems.json to Zetaris SQL."""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
GEMS_JSON = ROOT / "infra/zetaris/data/hidden-gems.json"
OUT_SQL = ROOT / "infra/zetaris/sql/import-sydney-place-photos.sql"


def sql_escape(val: str) -> str:
    return val.replace("'", "''")


def main() -> None:
    data = json.loads(GEMS_JSON.read_text(encoding="utf-8"))
    places = data.get("places", [])

    sql_lines = [
        "-- Auto-generated Sydney Attraction Verified Photos Import for Zetaris Data Lakehouse",
        "CREATE TABLE IF NOT EXISTS zetaris_sydney_place_photos (",
        "    id VARCHAR(128) PRIMARY KEY,",
        "    name VARCHAR(255) NOT NULL,",
        "    photo_count INT NOT NULL,",
        "    primary_photo_url VARCHAR(1024),",
        "    primary_photo_title VARCHAR(512),",
        "    primary_photo_artist VARCHAR(255),",
        "    primary_photo_license VARCHAR(128),",
        "    match_kind VARCHAR(64),",
        "    photos_json TEXT",
        ");",
        "",
    ]

    for p in places:
        photos = p.get("photos") or []
        first = photos[0] if photos else {}
        photos_json = json.dumps(photos, ensure_ascii=False)
        sql_lines.append(
            "INSERT INTO zetaris_sydney_place_photos "
            "(id, name, photo_count, primary_photo_url, primary_photo_title, primary_photo_artist, primary_photo_license, match_kind, photos_json) VALUES ("
            f"'{sql_escape(p['id'])}', "
            f"'{sql_escape(p['name'])}', "
            f"{len(photos)}, "
            f"'{sql_escape(first.get('url', ''))}', "
            f"'{sql_escape(first.get('title', ''))}', "
            f"'{sql_escape(first.get('artist', ''))}', "
            f"'{sql_escape(first.get('license', ''))}', "
            f"'{sql_escape(first.get('matchKind', ''))}', "
            f"'{sql_escape(photos_json)}'"
            ");"
        )

    OUT_SQL.write_text("\n".join(sql_lines) + "\n", encoding="utf-8")
    print(f"Exported {len(places)} attraction photo records ({sum(len(p.get('photos', [])) for p in places)} photos) to {OUT_SQL}")


if __name__ == "__main__":
    main()
