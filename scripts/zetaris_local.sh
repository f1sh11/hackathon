#!/usr/bin/env bash
# Operate an existing OFFICIAL installation, without copying credentials or deleting volumes.
set -euo pipefail
usage() {
  cat <<'HELP'
Usage: bash scripts/zetaris_local.sh <start|status|verify|stop> [platform-directory]
Default directory: $ZETARIS_PLATFORM_DIR, or $HOME/zetaris-local/zetaris-platform
Requires an official Zetaris distribution, its matching private .env and Docker Compose.
start: start/resume the platform; status: show services; verify: run vendor verifier;
stop: stop containers, retaining data. No registry login, cloud deployment or data import.
HELP
}
action="${1:-help}"
case "$action" in help|-h|--help) usage; exit 0;; start|status|verify|stop) ;; *) usage >&2; exit 2;; esac
if [ "$#" -gt 2 ]; then usage >&2; exit 2; fi
platform="${2:-${ZETARIS_PLATFORM_DIR:-$HOME/zetaris-local/zetaris-platform}}"
[ -d "$platform" ] || { echo 'Official platform directory does not exist. See SERVICE-STARTUP.md.' >&2; exit 1; }
platform="$(cd "$platform" && pwd -P)"
[ -f "$platform/.env" ] || { echo 'Missing official private .env; do not generate replacement platform credentials.' >&2; exit 1; }
compose="$platform/docker-compose.yml"
[ -f "$compose" ] || { echo 'Missing official docker-compose.yml.' >&2; exit 1; }
if command -v docker >/dev/null 2>&1; then docker_bin="$(command -v docker)"
elif [ -x "$HOME/.docker/bin/docker" ]; then docker_bin="$HOME/.docker/bin/docker"
else echo 'Docker is not available; install/start Docker using the official vendor guide.' >&2; exit 1; fi
"$docker_bin" compose version >/dev/null
"$docker_bin" info >/dev/null 2>&1 || { echo 'Docker daemon is not ready.' >&2; exit 1; }
case "$action" in
  start) "$docker_bin" compose --project-directory "$platform" --env-file "$platform/.env" -f "$compose" up -d;;
  status) "$docker_bin" compose --project-directory "$platform" --env-file "$platform/.env" -f "$compose" ps --all;;
  stop) "$docker_bin" compose --project-directory "$platform" --env-file "$platform/.env" -f "$compose" stop;;
  verify)
    [ -f "$platform/verify.sh" ] || { echo 'Missing vendor verify.sh.' >&2; exit 1; }
    # The vendor verifier invokes docker by name. Add the already resolved binary directory.
    (cd "$platform" && PATH="$(dirname "$docker_bin"):$PATH" bash ./verify.sh);;
esac
