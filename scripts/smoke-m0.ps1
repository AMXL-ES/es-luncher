#requires -Version 5.1
<#
.SYNOPSIS
  M0 冒烟：构建 Release → 起空窗口 → 测「进程启动 → 窗口可交互」毫秒数 → 正常关闭。
.DESCRIPTION
  判据来自 ROADMAP M0：空窗口能开能关，且冷启动有数字。
  不改任何系统设置；窗口由本进程拉起、本进程关闭。
#>
[CmdletBinding()]
param(
    [string]$ProjectDir,
    [string]$Configuration = 'Release',
    [int]$LaunchTimeoutSeconds = 30
)

$ErrorActionPreference = 'Stop'

if (-not $ProjectDir) {
    $ProjectDir = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
}

Push-Location $ProjectDir
try {
    $buildOut = & dotnet build GLauncher.slnx -c $Configuration --nologo 2>&1
    $buildCode = $LASTEXITCODE
    $buildOut | Select-Object -Last 5
    if ($buildCode -ne 0) { throw "build failed: exit $buildCode" }

    $binDir = Join-Path $ProjectDir "src/GLauncher.App/bin/$Configuration"
    $exe = Get-ChildItem -Path $binDir -Filter 'GLauncher.exe' -Recurse -File |
           Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $exe) { throw "找不到 GLauncher.exe：$binDir" }

    "exe: $($exe.FullName)"
    $sizeMb = [math]::Round($exe.Length / 1MB, 1)

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $proc = Start-Process -FilePath $exe.FullName -PassThru
    $windowMs = $null
    while ($sw.Elapsed.TotalSeconds -lt $LaunchTimeoutSeconds) {
        $proc.Refresh()
        if ($proc.HasExited) { break }
        if ($proc.MainWindowHandle -ne [IntPtr]::Zero) {
            $windowMs = [math]::Round($sw.Elapsed.TotalMilliseconds)
            break
        }
        Start-Sleep -Milliseconds 10
    }
    $sw.Stop()

    if ($null -eq $windowMs) {
        if ($proc.HasExited) { throw "进程在出窗口前退出，exit=$($proc.ExitCode)" }
        $proc.Kill()
        throw "窗口 ${LaunchTimeoutSeconds}s 内未出现"
    }

    "cold-start(window ready) = ${windowMs} ms"
    "process working set = $([math]::Round($proc.WorkingSet64 / 1MB, 1)) MB"

    $null = $proc.CloseMainWindow()
    if (-not $proc.WaitForExit(5000)) {
        $proc.Kill()
        throw '窗口无法通过 CloseMainWindow 关闭（5s 超时）'
    }
    "closed cleanly, exit=$($proc.ExitCode), exe-size=$sizeMb MB"
    exit 0
}
catch {
    "FAIL: $($_.Exception.Message)"
    exit 1
}
finally {
    Pop-Location
}
