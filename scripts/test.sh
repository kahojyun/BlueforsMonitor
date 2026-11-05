#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet build -c Release -p:UseSharedCompilation=false
dotnet run --project tests/BlueforsMonitor.Tests -c Release --no-build
