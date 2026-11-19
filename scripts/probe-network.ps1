#requires -Version 5.1
<#
.SYNOPSIS
  GLauncher 网络探活：实测官方源 / 镜像 / 认证 / 模组站的可达性、状态码与延迟。
.DESCRIPTION
  只读探测，不写工程文件。依赖 Windows 自带的 curl.exe（Win10 1803+ 均自带）。
  退出码 0 = 全部符合预期；1 = 有目标不符合预期。
.EXAMPLE
  powershell -NoProfile -File scripts\probe-network.ps1
.EXAMPLE
  powershell -NoProfile -File scripts\probe-network.ps1 -JsonOut D:\mc-data\logs\net.json
#>
[CmdletBinding()]
param(
    [string]$JsonOut,
    [int]$TimeoutSeconds = 15
)

$ErrorActionPreference = 'Continue'

$curl = (Get-Command curl.exe -ErrorAction SilentlyContinue).Source
if (-not $curl) { 'FAIL: 找不到 curl.exe（Windows 10 1803+ 自带）'; exit 1 }

# Expect 是「该端点在无凭据下的正确结果」，不是「成功」：
# 401/403/404/302 都可能是预期答案，只要与 Expect 一致就算可达。
$targets = @(
    [pscustomobject]@{ Name = 'mojang-manifest';           Url = 'https://piston-meta.mojang.com/mc/game/version_manifest_v2.json'; Expect = 200; Note = '版本清单主源' }
    [pscustomobject]@{ Name = 'mojang-libraries';          Url = 'https://libraries.minecraft.net/';                             Expect = 404; Note = '根路径无索引，404 即可达' }
    [pscustomobject]@{ Name = 'mojang-assets';             Url = 'https://resources.download.minecraft.net/';                    Expect = 404; Note = '同上' }
    [pscustomobject]@{ Name = 'bmclapi-manifest';          Url = 'https://bmclapi2.bangbang93.com/mc/game/version_manifest_v2.json'; Expect = 302; Note = '镜像，需跟随重定向' }
    [pscustomobject]@{ Name = 'microsoft-login';           Url = 'https://login.microsoftonline.com/';                           Expect = 302; Note = 'M3 认证用' }
    [pscustomobject]@{ Name = 'minecraftservices-profile'; Url = 'https://api.minecraftservices.com/minecraft/profile';          Expect = 401; Note = '无 token 应为 401' }
    [pscustomobject]@{ Name = 'modrinth-api';              Url = 'https://api.modrinth.com/v2/tag/game_version';                 Expect = 200; Note = 'M8 模组检索' }
    [pscustomobject]@{ Name = 'curseforge-api';            Url = 'https://api.curseforge.com/v1/games';                          Expect = 403; Note = '需 API key（M8 前申请）' }
)

$results = @()
foreach ($t in $targets) {
    $curlArgs = @(
        '--silent', '--show-error',
        '--max-time', $TimeoutSeconds,
        '--output', 'NUL',
        '--write-out', '%{http_code} %{time_total} %{time_starttransfer}',
        $t.Url
    )
    $raw = (& $curl @curlArgs 2>&1) -join ' '
    $curlExit = $LASTEXITCODE

    $status = 'ERR'
    $totalMs = $null
    $ttfbMs = $null
    $err = ''
    if ($curlExit -eq 0 -and $raw -match '^(\d{3})\s+([\d.]+)\s+([\d.]+)') {
        $status = [int]$Matches[1]
        $totalMs = [math]::Round([double]$Matches[2] * 1000)
        $ttfbMs = [math]::Round([double]$Matches[3] * 1000)
    }
    else {
        $err = "curl exit=$curlExit $raw".Trim()
    }

    $results += [pscustomobject]@{
        Name   = $t.Name
        Status = $status
        Expect = $t.Expect
        TotalMs = $totalMs
        TtfbMs = $ttfbMs
        Ok     = ($status -ne 'ERR' -and [int]$status -eq $t.Expect)
        Note   = $t.Note
        Error  = $err
    }
}

$results | Format-Table Name, Status, Expect, TotalMs, TtfbMs, Ok, Note -AutoSize

$proxy = [ordered]@{
    HTTP_PROXY  = $env:HTTP_PROXY
    HTTPS_PROXY = $env:HTTPS_PROXY
    ALL_PROXY   = $env:ALL_PROXY
    NO_PROXY    = $env:NO_PROXY
}
'proxy: HTTP_PROXY=[{0}] HTTPS_PROXY=[{1}]' -f $proxy.HTTP_PROXY, $proxy.HTTPS_PROXY

if ($JsonOut) {
    $dir = Split-Path -Parent $JsonOut
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    $payload = [ordered]@{
        capturedAt     = (Get-Date).ToString('o')
        timeoutSeconds = $TimeoutSeconds
        proxy          = $proxy
        results        = @($results)
    }
    $payload | ConvertTo-Json -Depth 5 | Set-Content -Path $JsonOut -Encoding UTF8
    "json -> $JsonOut"
}

$failed = @($results | Where-Object { -not $_.Ok })
if ($failed.Count -gt 0) {
    "FAIL: $($failed.Count)/$($results.Count) 个目标与预期不符：$($failed.Name -join ', ')"
    exit 1
}

"OK: $($results.Count)/$($results.Count) 个目标符合预期"
exit 0
