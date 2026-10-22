#!/bin/sh
set -eu
cd "$(dirname "$0")/.."
if ! command -v dotnet >/dev/null 2>&1; then
    echo 'Please install the .NET 10 SDK and reopen this terminal.' >&2
    exit 1
fi
exec dotnet run --project server-dotnet/SydneyBuddy.Api
