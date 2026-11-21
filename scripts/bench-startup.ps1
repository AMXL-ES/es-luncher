#requires -version 5.1
<#
  M11 benchmark: cold start from process start to a usable main window.

  Runs N iterations, reports every sample plus median / p95 / max, and working set.
  Output: console table + optional JSON file.

  Usage:
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts\bench-startup.ps1
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts\bench-startup.ps1 -Runs 7 -JsonOut D:\mc-data\logs\bench-startup.json
#>
[CmdletBinding()]
param(
    [int]$Runs = 5,
    [int]$TimeoutSeconds = 30,
    [string]$JsonOut = ''
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repo 'src\EsLuncher.App\bin\Release\net10.0\EsLuncher.exe'
if (-not (Test-Path $exe)) {
    throw "找不到 $exe；先 dotnet build EsLuncher.slnx -c Release"
}

$samples = @()
Write-Host ''
Write-Host ("cold start benchmark: {0} runs, exe = {1}" -f $Runs, $exe)

for ($i = 1; $i -le $Runs; $i++) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $process = Start-Process -FilePath $exe -PassThru
    $handle = 0
    while ($sw.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
        Start-Sleep -Milliseconds 10
        $process.Refresh()
        if ($process.HasExited) { break }
        if ($process.MainWindowHandle -ne 0) {
            $handle = $process.MainWindowHandle
            break
        }
    }
    $sw.Stop()

    if ($handle -eq 0) {
        throw ("第 {0} 次没等到主窗口（{1} s 超时，exited={2}）" -f $i, $TimeoutSeconds, $process.HasExited)
    }

    $process.Refresh()
    $workingSetMb = [math]::Round($process.WorkingSet64 / 1048576, 1)
    $startupMs = [math]::Round($sw.Elapsed.TotalMilliseconds, 0)
    $samples += [pscustomobject]@{ Run = $i; StartupMs = $startupMs; WorkingSetMb = $workingSetMb }

    Write-Host ("  run {0}: {1,6} ms   working set {2} MB" -f $i, $startupMs, $workingSetMb)

    $null = $process.CloseMainWindow()
    if (-not $process.WaitForExit(15000)) { $process.Kill() }
    Start-Sleep -Milliseconds 800
}

$sortedMs = ($samples | Sort-Object StartupMs).StartupMs
$medianMs = $sortedMs[[int][math]::Floor($sortedMs.Count / 2)]
$p95Index = [int][math]::Ceiling($sortedMs.Count * 0.95) - 1
if ($p95Index -lt 0) { $p95Index = 0 }
$p95Ms = $sortedMs[$p95Index]
$minMs = ($sortedMs | Measure-Object -Minimum).Minimum
$maxMs = ($sortedMs | Measure-Object -Maximum).Maximum
$medianWs = ($samples | Sort-Object WorkingSetMb).WorkingSetMb[[int][math]::Floor($samples.Count / 2)]

$summary = [pscustomobject]@{
    Runs        = $Runs
    MinMs       = $minMs
    MedianMs    = $medianMs
    P95Ms       = $p95Ms
    MaxMs       = $maxMs
    MedianWsMb  = $medianWs
    MeasuredAt  = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
    Samples     = $samples
}

Write-Host ''
Write-Host ("RESULT: median {0} ms / p95 {1} ms / max {2} ms / median working set {3} MB" -f $medianMs, $p95Ms, $maxMs, $medianWs)

if ($JsonOut) {
    $summary | ConvertTo-Json -Depth 4 | Set-Content -Path $JsonOut -Encoding UTF8
    Write-Host ("json -> {0}" -f $JsonOut)
}
