#requires -version 5.1
<#
  M11 benchmark: download throughput (bytes on disk after a full fetch / elapsed seconds).

  Uses a throw-away data root so nothing is served from cache; SHA1 verification stays on
  (the engine always verifies, so this measures verifiable throughput, not raw network).

  Usage:
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts\bench-download.ps1
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts\bench-download.ps1 -Source bmclapi -KeepData
#>
[CmdletBinding()]
param(
    [string]$Version = '1.7.10',
    [string]$Parts = 'assets',
    [ValidateSet('official', 'bmclapi')][string]$Source = 'official',
    [string]$DataRoot = 'D:\dev\temp\bench-data',
    [int]$Concurrency = 8,
    [switch]$KeepData,
    [string]$JsonOut = ''
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$cli = Join-Path $repo 'src\EsLuncher.Cli\bin\Release\net10.0\esluncher.exe'
if (-not (Test-Path $cli)) {
    throw "找不到 $cli；先 dotnet build EsLuncher.slnx -c Release"
}

if (Test-Path $DataRoot) { Remove-Item $DataRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $DataRoot | Out-Null

$env:ESLUNCHER_DOWNLOAD_SOURCE = $Source
Write-Host ("download benchmark: version {0}, parts {1}, source {2}, concurrency {3}" -f $Version, $Parts, $Source, $Concurrency)
Write-Host ("data root: {0} (fresh)" -f $DataRoot)

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$output = & $cli fetch --version $Version --only $Parts --concurrency $Concurrency --data $DataRoot 2>&1 | Out-String
$code = $LASTEXITCODE
$sw.Stop()
Remove-Item Env:\ESLUNCHER_DOWNLOAD_SOURCE -ErrorAction SilentlyContinue

$output.Trim() | ForEach-Object { "  " + $_ }

if ($code -ne 0) {
    throw "fetch 失败（exit $code）"
}

$files = Get-ChildItem $DataRoot -Recurse -File
$bytes = ($files | Measure-Object -Property Length -Sum).Sum
$mb = [math]::Round($bytes / 1048576, 1)
$seconds = [math]::Round($sw.Elapsed.TotalSeconds, 2)
$mbps = 0
if ($seconds -gt 0) { $mbps = [math]::Round($mb / $seconds, 2) }

$summary = [pscustomobject]@{
    Version        = $Version
    Parts          = $Parts
    Source         = $Source
    Concurrency    = $Concurrency
    Files          = $files.Count
    TotalMb        = $mb
    Seconds        = $seconds
    MbPerSecond    = $mbps
    MeasuredAt     = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
}

Write-Host ''
Write-Host ("RESULT: {0} files / {1} MB in {2}s = {3} MB/s (source {4})" -f $summary.Files, $mb, $seconds, $mbps, $Source)

if ($JsonOut) {
    $summary | ConvertTo-Json -Depth 3 | Set-Content -Path $JsonOut -Encoding UTF8
    Write-Host ("json -> {0}" -f $JsonOut)
}

if (-not $KeepData) {
    Remove-Item $DataRoot -Recurse -Force
    Write-Host ("cleaned {0}" -f $DataRoot)
}
