#requires -version 5.1
<#
  M11 benchmark: idle memory over time (hang test). Samples working set every N seconds,
  writes a CSV plus a summary (first / last / peak / growth), then closes the app.

  Usage:
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts\bench-memory.ps1
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts\bench-memory.ps1 -Minutes 20 -IntervalSeconds 30
#>
[CmdletBinding()]
param(
    [int]$Minutes = 20,
    [int]$IntervalSeconds = 30,
    [string]$CsvOut = 'D:\mc-data\logs\bench-memory.csv',
    [string]$JsonOut = 'D:\mc-data\logs\bench-memory.json'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repo 'src\EsLuncher.App\bin\Release\net10.0\EsLuncher.exe'
if (-not (Test-Path $exe)) {
    throw "找不到 $exe；先 dotnet build EsLuncher.slnx -c Release"
}

$deadline = (Get-Date).AddMinutes($Minutes)
$rows = @()

$process = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 3
$process.Refresh()
if ($process.HasExited) { throw "启动器启动即退出，exit=$($process.ExitCode)" }

Write-Host ("hang test: {0} min, sample every {1}s, pid {2}" -f $Minutes, $IntervalSeconds, $process.Id)

$index = 0
while ((Get-Date) -lt $deadline) {
    $process.Refresh()
    if ($process.HasExited) {
        Write-Host "进程提前退出，stop at sample $index"
        break
    }

    $ws = [math]::Round($process.WorkingSet64 / 1048576, 2)
    $private = [math]::Round($process.PrivateMemorySize64 / 1048576, 2)
    $elapsed = [math]::Round(((Get-Date) - $process.StartTime).TotalMinutes, 2)
    $rows += [pscustomobject]@{
        Sample = $index
        ElapsedMin = $elapsed
        WorkingSetMb = $ws
        PrivateMb = $private
    }
    Write-Host ("  +{0,5:N1} min  working set {1,7} MB  private {2,7} MB" -f $elapsed, $ws, $private)
    $index++
    Start-Sleep -Seconds $IntervalSeconds
}

$process.Refresh()
if (-not $process.HasExited) {
    $null = $process.CloseMainWindow()
    if (-not $process.WaitForExit(15000)) { $process.Kill() }
}

if ($rows.Count -eq 0) { throw '没有采到样本' }

$first = $rows[0]
$last = $rows[$rows.Count - 1]
$peak = ($rows | Measure-Object -Property WorkingSetMb -Maximum).Maximum
$growth = [math]::Round($last.WorkingSetMb - $first.WorkingSetMb, 2)
$perHour = 0
if ($last.ElapsedMin -gt $first.ElapsedMin) {
    $perHour = [math]::Round($growth / ($last.ElapsedMin - $first.ElapsedMin) * 60, 2)
}

$rows | Export-Csv -Path $CsvOut -NoTypeInformation -Encoding UTF8

$summary = [pscustomobject]@{
    Minutes          = $Minutes
    Samples          = $rows.Count
    StartMb          = $first.WorkingSetMb
    EndMb            = $last.WorkingSetMb
    PeakMb           = $peak
    GrowthMb         = $growth
    GrowthMbPerHour  = $perHour
    MeasuredAt       = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
}

Write-Host ''
Write-Host ("RESULT: start {0} MB -> end {1} MB (growth {2} MB, ~{3} MB/h), peak {4} MB" -f $first.WorkingSetMb, $last.WorkingSetMb, $growth, $perHour, $peak)
Write-Host ("csv -> {0}" -f $CsvOut)

if ($JsonOut) {
    $summary | ConvertTo-Json -Depth 3 | Set-Content -Path $JsonOut -Encoding UTF8
    Write-Host ("json -> {0}" -f $JsonOut)
}
