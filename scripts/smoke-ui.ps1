# UI 冒烟：启动 GUI，等 8 秒确认没崩，再关掉。
# 需要已构建：dotnet build -c Release
$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot '..\src\EsLuncher.App\bin\Release\net10.0\EsLuncher.exe'
if (-not (Test-Path $exe)) {
    throw "未找到 $exe，请先 dotnet build EsLuncher.slnx -c Release"
}

$env:ESLUNCHER_UI_PAGE = 'instances'
$process = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 8
$process.Refresh()
if ($process.HasExited) {
    throw "启动器 8 秒内退出，exit=$($process.ExitCode)"
}

"UI smoke OK：进程 $($process.Id) 存活，title=$($process.MainWindowTitle)"
$null = $process.CloseMainWindow()
Start-Sleep -Seconds 2
if (-not $process.HasExited) {
    $process.Kill()
}
