#!/bin/sh
set -eu
cd "$(dirname "$0")/.."
exec sh scripts/start-server.sh
