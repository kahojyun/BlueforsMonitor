#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet restore --locked-mode
python3 scripts/check_dependencies.py
dotnet build --no-restore -c Release -p:UseSharedCompilation=false
dotnet run --project tests/BlueforsMonitor.Tests -c Release --no-build
