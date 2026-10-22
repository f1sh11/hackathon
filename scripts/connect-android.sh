#!/usr/bin/env sh
set -eu
if [ -n "${ANDROID_HOME:-}" ]; then
  adb="$ANDROID_HOME/platform-tools/adb"
else
  adb=adb
fi
if [ "$#" -gt 0 ]; then
  "$adb" -s "$1" reverse tcp:8787 tcp:8787
else
  "$adb" reverse tcp:8787 tcp:8787
fi
