param([string]$Runtime = "win-x64")
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")
python scripts/release.py build --runtime $Runtime
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
