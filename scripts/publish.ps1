param([string]$Runtime = "win-x64")
$ErrorActionPreference = "Stop"
if ($Runtime -ne "win-x64") { throw "The native launcher currently supports win-x64 only." }
Set-Location (Join-Path $PSScriptRoot "..")
dotnet publish src/BlueforsMonitor.Desktop -c Release -r $Runtime --self-contained true -p:RestoreLockedMode=true -p:UseSharedCompilation=false -o "artifacts/$Runtime"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
python scripts/package_windows.py "artifacts/$Runtime" "artifacts/BlueforsMonitor-$Runtime.zip"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
