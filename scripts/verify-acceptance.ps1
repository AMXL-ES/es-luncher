#requires -version 5.1
<#
  ES Luncher acceptance verification (M0-M9).

  Non-destructive by default: downloads use --dry-run only, launches use --dry-run only.
  -WithLaunch adds ONE real launch with --autostop (closes the game on its own).
  -WithUi grabs the 8s UI smoke run.

  Exit code 0 = every executed check passed.
  Usage:
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts\verify-acceptance.ps1
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts\verify-acceptance.ps1 -WithUi -WithLaunch
#>
[CmdletBinding()]
param(
  [string]$DataRoot = 'D:\mc-data',
  [switch]$WithUi,
  [switch]$WithLaunch,
  [string]$ResultFile
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo

$script:results = New-Object System.Collections.ArrayList

function Add-Result {
  param([string]$Name, [int]$Code, [string]$Detail, [string]$Output)

  $status = 'PASS'
  if ($Code -ne 0) { $status = 'FAIL' }
  $null = $script:results.Add([pscustomobject]@{
    Check  = $Name
    Exit   = $Code
    Status = $status
    Detail = $Detail
  })
  Write-Host ("[{0}] {1,-26} exit={2}  {3}" -f $status, $Name, $Code, $Detail)
  if ($Code -ne 0 -and $Output) {
    $tail = ($Output -split "`r?`n" | Select-Object -Last 6) -join "`n"
    Write-Host ("       > " + ($tail -replace "`n", "`n       > "))
  }
}

function Invoke-Step {
  param(
    [string]$Name,
    [scriptblock]$Action,
    [string]$Detail = ''
  )

  $sw = [System.Diagnostics.Stopwatch]::StartNew()
  $out = ''
  $code = 0
  try {
    $out = (& $Action 2>&1 | Out-String)
    $code = $LASTEXITCODE
    if ($null -eq $code) { $code = 0 }
  }
  catch {
    $code = 1
    $out = $_.Exception.Message
  }
  $sw.Stop()
  $detail = "{0} ({1:N1}s)" -f $Detail, $sw.Elapsed.TotalSeconds
  Add-Result -Name $Name -Code $code -Detail $detail -Output $out
  return $out
}

function Invoke-Cli {
  param([string[]]$CliArgs)

  $out = & $script:Exe @CliArgs --data $DataRoot 2>&1 | Out-String
  $script:LastCliCode = $LASTEXITCODE
  $script:LastCliOut = $out
}

Write-Host ''
Write-Host '========================================='
Write-Host ' ES Luncher acceptance verification'
Write-Host (" repo     : {0}" -f $repo)
Write-Host (" data root: {0}" -f $DataRoot)
Write-Host (" started  : {0}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
Write-Host '========================================='
Write-Host ''

# ---------------------------------------------------------------- M0 build
$null = Invoke-Step -Name 'build-release' -Detail 'dotnet build -c Release' -Action {
  dotnet build EsLuncher.slnx -c Release --nologo -v m
}

# ---------------------------------------------------------------- M0 tests
$testOut = Invoke-Step -Name 'test-all' -Detail 'dotnet test -c Release' -Action {
  dotnet test EsLuncher.slnx -c Release --nologo
}
$testSummary = '-'
$m = [regex]::Match($testOut, 'through:\s*(\d+)|passed:\s*(\d+)')
if (-not $m.Success) { $m = [regex]::Match($testOut, 'Total:\s*(\d+)') }
$totalMatch = [regex]::Matches($testOut, '\u901a\u8fc7:\s*(\d+)')
if ($totalMatch.Count -gt 0) {
  $sum = 0
  foreach ($x in $totalMatch) { $sum += [int]$x.Groups[1].Value }
  $testSummary = "passed=$sum"
}
$failedMatch = [regex]::Matches($testOut, '\u5931\u8d25:\s*(\d+)')
if ($failedMatch.Count -gt 0) {
  $fsum = 0
  foreach ($x in $failedMatch) { $fsum += [int]$x.Groups[1].Value }
  $testSummary = "$testSummary failed=$fsum"
}
$script:results[$script:results.Count - 1].Detail = $testSummary

# ---------------------------------------------------------------- CLI surface
$script:Exe = Join-Path $repo 'src\EsLuncher.Cli\bin\Release\net10.0\esluncher.exe'
if (-not (Test-Path $script:Exe)) {
  Add-Result -Name 'cli-exe' -Code 1 -Detail "not found: $($script:Exe)"
  $exeOk = $false
}
else {
  Add-Result -Name 'cli-exe' -Code 0 -Detail (Split-Path -Leaf $script:Exe)
  $exeOk = $true
}

if ($exeOk) {
  # -------------------------------------------------------------- M1 versions
  $null = Invoke-Step -Name 'M1 versions' -Detail 'release list' -Action {
    Invoke-Cli -CliArgs @('versions', '--type', 'release', '--limit', '5')
    $script:LastCliCode = $LASTEXITCODE
    if ($script:LastCliCode -eq 0 -and $script:LastCliOut -notmatch '1\.') {
      $script:LastCliCode = 1
    }
    return $script:LastCliOut
  }

  # -------------------------------------------------------------- M1 fetch plan
  $null = Invoke-Step -Name 'M1 fetch dry-run' -Detail '1.7.10 plan' -Action {
    Invoke-Cli -CliArgs @('fetch', '--version', '1.7.10', '--dry-run')
    if ($script:LastCliCode -eq 0 -and $script:LastCliOut -notmatch 'natives|MiB|files') {
      $script:LastCliCode = 1
    }
    return $script:LastCliOut
  }

  # -------------------------------------------------------------- M2 launch plan
  $null = Invoke-Step -Name 'M2 launch dry-run' -Detail '1.18.2 command' -Action {
    Invoke-Cli -CliArgs @('launch', '--version', '1.18.2', '--username', 'Dev', '--dry-run')
    if ($script:LastCliCode -eq 0 -and $script:LastCliOut -notmatch '\-cp|classpath') {
      $script:LastCliCode = 1
    }
    return $script:LastCliOut
  }

  # -------------------------------------------------------------- M4 java runtimes
  $javaOut = Invoke-Step -Name 'M4 java list' -Detail 'discovered runtimes' -Action {
    Invoke-Cli -CliArgs @('java')
    if ($script:LastCliCode -eq 0 -and $script:LastCliOut -notmatch 'Java\s*\d') {
      $script:LastCliCode = 1
    }
    return $script:LastCliOut
  }

  # -------------------------------------------------------------- M5 instances
  $null = Invoke-Step -Name 'M5 instance list' -Detail 'instance store' -Action {
    Invoke-Cli -CliArgs @('instance', 'list')
    if ($script:LastCliCode -eq 0 -and $script:LastCliOut.Trim().Length -eq 0) {
      $script:LastCliCode = 1
    }
    return $script:LastCliOut
  }

  $null = Invoke-Step -Name 'M5 instance size' -Detail 'disk accounting' -Action {
    Invoke-Cli -CliArgs @('instance', 'size', 'Fabric 测试')
    return $script:LastCliOut
  }

  $null = Invoke-Step -Name 'M5 launch dry-run' -Detail 'instance-scoped launch' -Action {
    Invoke-Cli -CliArgs @('launch', '--instance', 'Fabric 测试', '--dry-run')
    return $script:LastCliOut
  }

  # -------------------------------------------------------------- M8 content
  $null = Invoke-Step -Name 'M8 content list' -Detail 'mods of Fabric test' -Action {
    Invoke-Cli -CliArgs @('content', 'list', '--instance', 'Fabric 测试', '--kind', 'mods')
    return $script:LastCliOut
  }

  # -------------------------------------------------------------- M9 diagnostics
  $null = Invoke-Step -Name 'M9 deps' -Detail 'dependency health' -Action {
    Invoke-Cli -CliArgs @('deps', '--instance', 'Fabric 测试')
    return $script:LastCliOut
  }

  $null = Invoke-Step -Name 'M9 diagnose' -Detail 'error lines + hints' -Action {
    Invoke-Cli -CliArgs @('diagnose', '--instance', 'Fabric 测试')
    return $script:LastCliOut
  }

  # -------------------------------------------------------------- M3 accounts
  $null = Invoke-Step -Name 'M3 accounts' -Detail 'saved accounts (no token)' -Action {
    Invoke-Cli -CliArgs @('accounts')
    return $script:LastCliOut
  }

  # -------------------------------------------------------------- M7 UI smoke
  if ($WithUi) {
    $uiOut = Invoke-Step -Name 'M7 UI smoke' -Detail 'window alive 8s' -Action {
      & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'scripts\smoke-ui.ps1') 2>&1 | Out-String
    }
    if ($uiOut -notmatch 'UI smoke OK') {
      $last = $script:results[$script:results.Count - 1]
      $last.Status = 'FAIL'
      $last.Exit = 1
      Write-Host '[FAIL] M7 UI smoke      (no "UI smoke OK" in output)'
    }
  }

  # -------------------------------------------------------------- real launch
  if ($WithLaunch) {
    $null = Invoke-Step -Name 'REAL launch' -Detail 'instance + autostop 25' -Action {
      Invoke-Cli -CliArgs @('launch', '--instance', 'Fabric 测试', '--autostop', '25')
      return $script:LastCliOut
    }
  }
}

# ---------------------------------------------------------------- summary
$passed = @($script:results | Where-Object { $_.Status -eq 'PASS' }).Count
$failed = @($script:results | Where-Object { $_.Status -eq 'FAIL' }).Count
$summary = $script:results | Format-Table -AutoSize | Out-String

Write-Host ''
Write-Host '========================================='
Write-Host (" RESULT: {0} passed / {1} failed / {2} total" -f $passed, $failed, $script:results.Count)
Write-Host (" finished: {0}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
Write-Host '========================================='

if ($ResultFile) {
  $report = @()
  $report += "# ES Luncher acceptance run"
  $report += ""
  $report += "- time: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
  $report += "- data root: $DataRoot"
  $report += "- result: **$passed passed / $failed failed / $($script:results.Count) total**"
  $report += ""
  $report += "| check | exit | status | detail |"
  $report += "|---|---|---|---|"
  foreach ($r in $script:results) {
    $report += ("| {0} | {1} | {2} | {3} |" -f $r.Check, $r.Exit, $r.Status, $r.Detail)
  }
  $report | Set-Content -Path $ResultFile -Encoding UTF8
  Write-Host (" report -> {0}" -f $ResultFile)
}

if ($failed -gt 0) { exit 1 }
exit 0
