#requires -version 5.1
<#
  Build a Velopack release (Setup.exe + portable zip + delta packages) for ES Luncher.

  Output lands in artifacts\releases by default. Packing a SECOND version into the SAME
  output directory is what produces delta packages, so keep releases together:

    powershell -NoProfile -File scripts\pack-velopack.ps1 -Version 0.1.0
    powershell -NoProfile -File scripts\pack-velopack.ps1 -Version 0.1.1

  vpk is not on PATH by design (no system pollution). Install it once with:
    dotnet tool install vpk --tool-path D:\dev\tools\vpk
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$OutputDir = '',
    [string]$VpkPath = 'D:\dev\tools\vpk\vpk.exe',
    [switch]$SelfContained,
    [switch]$NoDelta
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $root 'artifacts\releases'
}

if (-not (Test-Path $VpkPath)) {
    throw "找不到 vpk：$VpkPath。先执行 dotnet tool install vpk --tool-path D:\dev\tools\vpk"
}

$publishDir = Join-Path $root ("artifacts\publish\" + $Version)
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

$project = Join-Path $root 'src\EsLuncher.App\EsLuncher.App.csproj'
$icon = Join-Path $root 'src\EsLuncher.App\Assets\es-luncher.ico'

Write-Host "publish -> $publishDir (selfContained=$($SelfContained.IsPresent), version=$Version)"
$selfContainedArg = if ($SelfContained.IsPresent) { 'true' } else { 'false' }
dotnet publish $project -c Release -r win-x64 --self-contained $selfContainedArg `
    -p:Version=$Version -p:AssemblyVersion="$Version.0" -p:FileVersion="$Version.0" `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败（exit $LASTEXITCODE）" }

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$delta = if ($NoDelta.IsPresent) { 'None' } else { 'BestSpeed' }
$arguments = @(
    'pack',
    '--packId', 'ESLuncher',
    '--packVersion', $Version,
    '--packDir', $publishDir,
    '--mainExe', 'EsLuncher.exe',
    '--runtime', 'win-x64',
    '--packTitle', 'ES Luncher',
    '--packAuthors', 'Endless Studio',
    '--outputDir', $OutputDir,
    '--delta', $delta
)
if (Test-Path $icon) { $arguments += @('--icon', $icon) }

Write-Host ("vpk " + ($arguments -join ' '))
& $VpkPath @arguments
if ($LASTEXITCODE -ne 0) { throw "vpk pack 失败（exit $LASTEXITCODE）" }

Write-Host ''
Write-Host "releases -> $OutputDir"
Get-ChildItem $OutputDir -File | Sort-Object LastWriteTime -Descending | Select-Object -First 8 |
    ForEach-Object { "  {0,-52} {1,10:N1} KB" -f $_.Name, ($_.Length / 1KB) }
