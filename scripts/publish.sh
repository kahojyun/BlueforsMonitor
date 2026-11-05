#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
python3 scripts/release.py build --runtime "${1:-osx-arm64}"
