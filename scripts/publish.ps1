param([string]$Runtime = "win-x64")
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")
dotnet publish src/BlueforsMonitor.Desktop -c Release -r $Runtime --self-contained true -p:UseSharedCompilation=false -o "artifacts/$Runtime"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Copy-Item LICENSE, THIRD_PARTY_NOTICES.md -Destination "artifacts/$Runtime"
Copy-Item licenses -Destination "artifacts/$Runtime" -Recurse -Force
Compress-Archive -Path "artifacts/$Runtime/*" -DestinationPath "artifacts/BlueforsMonitor-$Runtime.zip" -Force
