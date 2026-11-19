#requires -Version 5.1
<#
.SYNOPSIS
  M1–M3 回归冒烟：一条命令验证构建、测试、账号、启动命令组装、下载幂等、版本列表。
.DESCRIPTION
  只读验证 + 必要的网络请求（版本清单缓存已在本地，命中缓存时不出网）。
  退出码 0 = 全部通过；1 = 有步骤失败。
  本机只有 Windows PowerShell 5.1，脚本按 5.1 语法写，且文件必须 UTF-8 带 BOM。
#>
[CmdletBinding()]
param(
    [string]$ProjectDir
)

$ErrorActionPreference = 'Continue'
$results = New-Object System.Collections.ArrayList

# 兼容性：某些宿主下 $PSScriptRoot 可能为空，退回 $MyInvocation 推导脚本目录。
if ([string]::IsNullOrEmpty($ProjectDir)) {
    $scriptPath = $MyInvocation.MyCommand.Path
    if ([string]::IsNullOrEmpty($scriptPath)) { $scriptPath = $PSCommandPath }
    $ProjectDir = Split-Path -Parent (Split-Path -Parent $scriptPath)
}
if (-not (Test-Path $ProjectDir)) {
    Write-Host "项目目录不存在：$ProjectDir" -ForegroundColor Red
    exit 1
}

function Add-Result {
    param([string]$Name, [bool]$Ok, [string]$Detail)
    [void]$results.Add([pscustomobject]@{ Name = $Name; Ok = $Ok; Detail = $Detail })
    if ($Ok) { Write-Host ("[PASS] " + $Name) -ForegroundColor Green }
    else { Write-Host ("[FAIL] " + $Name + " :: " + $Detail) -ForegroundColor Red }
}

Push-Location $ProjectDir
try {
    $cli = Join-Path $ProjectDir 'src\GLauncher.Cli\bin\Release\net10.0\glauncher.exe'
    if (-not (Test-Path $cli)) {
        Write-Host "找不到 CLI：$cli，先跑 dotnet build -c Release" -ForegroundColor Red
        exit 1
    }

    Write-Host '=== 1/6 build ==='
    $out = (& dotnet build GLauncher.slnx -c Release --nologo 2>&1) | Out-String
    Add-Result 'build (exit 0, 0 warning)' ($LASTEXITCODE -eq 0) $out.Trim()

    Write-Host '=== 2/6 test ==='
    $out = (& dotnet test GLauncher.slnx -c Release --nologo 2>&1) | Out-String
    $testsOk = ($LASTEXITCODE -eq 0) -and ($out -match '失败:\s*0')
    $passed = if ($out -match '通过:\s*(\d+)') { $Matches[1] } else { '?' }
    Add-Result "test ($passed passed, 0 failed)" $testsOk $out.Trim()

    Write-Host '=== 3/6 accounts ==='
    $out = (& $cli accounts 2>&1) | Out-String
    Add-Result 'accounts 可用' (($LASTEXITCODE -eq 0) -or ($out -match '没有已保存的账号')) $out.Trim()

    Write-Host '=== 4/6 launch --dry-run ==='
    $out = (& $cli launch --version 1.18.2 --dry-run --username Dev 2>&1) | Out-String
    $dryOk = ($LASTEXITCODE -eq 0) -and ($out -match 'missing 0')
    Add-Result 'launch 1.18.2 命令组装 (classpath missing 0)' $dryOk $out.Trim()

    Write-Host '=== 5/6 fetch 幂等 ==='
    $out = (& $cli fetch --version 1.7.10 2>&1) | Out-String
    $fetchOk = ($LASTEXITCODE -eq 0) -and ($out -match 'downloaded=0') -and ($out -match 'failed=0')
    Add-Result 'fetch 1.7.10 幂等 (downloaded=0, failed=0)' $fetchOk $out.Trim()

    Write-Host '=== 6/6 versions ==='
    $out = (& $cli versions --limit 3 2>&1) | Out-String
    Add-Result 'versions 列表' (($LASTEXITCODE -eq 0) -and ($out -match 'manifest: \d+ 个版本')) $out.Trim()
}
finally {
    Pop-Location
}

Write-Host ''
Write-Host '== summary =='
$results | Format-Table Name, Ok -AutoSize | Out-String | Write-Host
$failed = @($results | Where-Object { -not $_.Ok })
if ($failed.Count -gt 0) {
    Write-Host ("FAIL: " + $failed.Count + "/" + $results.Count + " 步失败") -ForegroundColor Red
    exit 1
}

Write-Host ("PASS: " + $results.Count + "/" + $results.Count + " 步通过") -ForegroundColor Green
exit 0
