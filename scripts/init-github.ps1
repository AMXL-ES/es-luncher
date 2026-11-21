#requires -version 5.1
<#
  一次性把本地仓库接到 GitHub（不创建仓库、不处理认证 —— 那两步在网页/凭据管理器里做）。

  用法：
    powershell -NoProfile -File scripts\init-github.ps1 -Owner <你的GitHub用户名>
    powershell -NoProfile -File scripts\init-github.ps1 -Owner <owner> -Repo es-luncher -Push
    # 顺带把以后的提交身份换成真实的：
    powershell -NoProfile -File scripts\init-github.ps1 -Owner <owner> -Name "你的名字" -Email "you@example.com" -Push

  它只做三件事：校验状态 → 加 origin → （可选）push -u origin main。
  已存在 origin 时直接报错退出，不会覆盖。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Owner,
    [string]$Repo = 'es-luncher',
    [string]$Name = '',
    [string]$Email = '',
    [switch]$Push
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not (Test-Path (Join-Path $root '.git'))) {
    throw "这里不是 git 仓库：$root"
}

$existing = @(git remote)
if ($existing -contains 'origin') {
    $url = git remote get-url origin
    throw "已经存在 origin（$url）。要换地址请先执行：git remote set-url origin <新地址>"
}

$branch = git branch --show-current
if ($branch -ne 'main') {
    Write-Warning "当前分支是 '$branch'，不是 main。脚本不会切换分支。"
}

if (-not [string]::IsNullOrWhiteSpace($Name)) {
    git config --local user.name $Name
    Write-Host "已设置本地 user.name = $Name"
}

if (-not [string]::IsNullOrWhiteSpace($Email)) {
    git config --local user.email $Email
    Write-Host "已设置本地 user.email = $Email"
}

$url = "https://github.com/$Owner/$Repo.git"
git remote add origin $url
Write-Host "已添加 origin -> $url"

Write-Host ''
Write-Host '接下来（按顺序）：'
Write-Host "  1. 在 https://github.com/new 建一个**空**仓库：$Owner/$Repo（不要勾 README / .gitignore / license）"
Write-Host '  2. 确认凭据可用（首次 push 会弹浏览器登录 Git Credential Manager）'
Write-Host "  3. git push -u origin $branch"

if ($Push) {
    Write-Host ''
    Write-Host "执行 git push -u origin $branch ..."
    git push -u origin $branch
    if ($LASTEXITCODE -ne 0) {
        throw "push 失败（exit $LASTEXITCODE）。常见原因：仓库还没在网页上建好，或凭据未登录。"
    }

    Write-Host "完成：https://github.com/$Owner/$Repo"
}
