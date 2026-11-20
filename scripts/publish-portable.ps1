# Build a portable zip: dotnet publish -> zip
# Usage:
#   powershell -File scripts\publish-portable.ps1 -Version 0.1.0-dev
#   powershell -File scripts\publish-portable.ps1 -Version 0.1.0-dev -SelfContained
param(
    [string]$Version = "0.1.0-dev",
    [string]$Runtime = "win-x64",
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\EsLuncher.App\EsLuncher.App.csproj"
$outDir = Join-Path $root "artifacts\EsLuncher-$Version-$Runtime"
$zip = Join-Path $root "artifacts\EsLuncher-$Version-$Runtime.zip"

New-Item -ItemType Directory -Force -Path (Join-Path $root "artifacts") | Out-Null
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }

Write-Host "publish -> $outDir (selfContained=$($SelfContained.IsPresent))"
dotnet publish $project -c Release -r $Runtime --self-contained:$($SelfContained.IsPresent) -o $outDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }

Copy-Item (Join-Path $root "README.md") $outDir -Force
Copy-Item (Join-Path $root "LICENSE") $outDir -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $root "docs\API-KEYS.md") $outDir -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $root "docs\RELEASE-CHECK.md") $outDir -Force -ErrorAction SilentlyContinue

if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $outDir "*") -DestinationPath $zip -Force
Write-Host "done -> $zip"
