#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
rid="${1:-osx-arm64}"
destination="artifacts/$rid"
dotnet publish src/BlueforsMonitor.Desktop -c Release -r "$rid" --self-contained true -p:RestoreLockedMode=true -p:UseSharedCompilation=false -o "$destination"
cp LICENSE THIRD_PARTY_NOTICES.md "$destination/"
cp -R licenses "$destination/"
if [[ "$rid" == osx-* ]]; then
  bundle="artifacts/Bluefors Monitor.app/Contents"
  mkdir -p "$bundle/MacOS"
  cp -R "$destination/." "$bundle/MacOS/"
  cp packaging/Info.plist "$bundle/Info.plist"
fi
