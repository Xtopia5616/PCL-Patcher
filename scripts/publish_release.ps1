#requires -Version 7.0
<#
.SYNOPSIS
    Publishes a PCL Patcher release: pushes the tag and creates the GitHub release.

.DESCRIPTION
    1. verifies the working tree is clean and the tag exists (creates an
       annotated tag when missing, using RELEASE-<tag>.md as the body);
    2. builds dist/ unless -SkipBuild is passed;
    3. pushes the tag and calls `gh release create` with the two binaries.

    Requires the GitHub CLI authenticated with repo scope:
        winget install --id GitHub.cli
        gh auth login

.PARAMETER Tag
    Release tag, e.g. v1.1.1.

.PARAMETER SkipBuild
    Reuse the existing dist/ binaries instead of rebuilding.

.EXAMPLE
    pwsh .\scripts\publish_release.ps1 -Tag v1.1.1
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Tag,

    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $root

if ($Tag -notmatch '^v\d+\.\d+\.\d+$') {
    throw "tag 必须形如 v1.1.1（当前：$Tag）"
}

$notes = Join-Path $root "RELEASE-$Tag.md"
if (-not (Test-Path $notes)) {
    throw "缺少发布说明：$notes"
}

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw "未安装 GitHub CLI：winget install --id GitHub.cli，然后 gh auth login"
}
gh auth status *> $null
if ($LASTEXITCODE -ne 0) {
    throw "gh 未登录：先执行 gh auth login（需要 repo 权限）"
}

$dirty = git status --porcelain
if ($dirty) {
    throw "工作区有未提交的改动，先提交或 stash：`n$dirty"
}

if (-not (git tag -l $Tag)) {
    Write-Host "== 创建 tag $Tag ==" -ForegroundColor Cyan
    git tag -a $Tag -F $notes
}

if (-not $SkipBuild) {
    Write-Host "== 构建 dist ==" -ForegroundColor Cyan
    & pwsh -NoProfile -File (Join-Path $root 'build.ps1')
    if ($LASTEXITCODE -ne 0) { throw "build.ps1 失败（退出码 $LASTEXITCODE）" }
}

$assets = @(
    (Join-Path $root 'dist\PclPatcher.exe'),
    (Join-Path $root 'dist\PclPatcher.Gui.Net48.exe')
)
foreach ($asset in $assets) {
    if (-not (Test-Path $asset)) { throw "缺少产物：$asset（去掉 -SkipBuild 重新构建）" }
}

Write-Host "== 推送 tag ==" -ForegroundColor Cyan
git push origin $Tag

Write-Host "== 创建 GitHub Release ==" -ForegroundColor Cyan
gh release create $Tag @assets `
    --title "PCL Patcher $($Tag.TrimStart('v'))" `
    --notes-file $notes `
    --latest

Write-Host "done: https://github.com/Xtopia5616/PCL-Patcher/releases/tag/$Tag" -ForegroundColor Green
