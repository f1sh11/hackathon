#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
./scripts/start.sh
if command -v open >/dev/null 2>&1; then
  open "http://$(docker compose port api 8080)"
fi
