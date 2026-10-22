#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
if ! command -v docker >/dev/null 2>&1; then
  echo '需要 Docker 与 Docker Compose。安装并启动 Docker Desktop 后再次运行此文件。' >&2
  exit 1
fi
docker compose version >/dev/null
docker compose up --build --detach --wait
ROAM_LOCAL_ADDRESS="$(docker compose port api 8080)"
printf '\n漫游 MVP 已启动：http://%s\n' "$ROAM_LOCAL_ADDRESS"
