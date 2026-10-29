#requires -Version 7.0
<#
.SYNOPSIS
    一键适配任意 PCL 构建：识别渠道/指纹 → 试运行 → 原地补丁（默认禁用自更新）。

.DESCRIPTION
    适配顺序完全交给 PclPatcher：
      exact      SHA-256 命中已收录构建；
      structural 7 个锚点指纹命中（新的快照重建走这条，不需要 --force）；
      unknown    两者都不命中，此时要么 -Adopt 收录，要么先在 GUI 里勾“允许未知版本”。

    收录（-Adopt）会写入 %LOCALAPPDATA%\PclPatcher\profiles.tsv，
    之后 CLI 与 GUI 都会直接识别该构建，不需要重新编译。

.EXAMPLE
    # 先看结论，不写文件
    pwsh .\scripts\adapt_new_pcl.ps1 "D:\Minecraft\Plain Craft Launcher 2.exe" -DryRun

.EXAMPLE
    # 新快照：结构匹配就直接补丁
    pwsh .\scripts\adapt_new_pcl.ps1 "C:\Users\me\Downloads\新的快照\Plain Craft Launcher 2.exe"

.EXAMPLE
    # 锚点移动过的新版本：先收录再补丁
    pwsh .\scripts\adapt_new_pcl.ps1 "新的快照.exe" -Adopt
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Pcl,

    [switch]$DryRun,

    [switch]$Adopt,

    [switch]$DownloadOnly,

    [switch]$KeepUpdate
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$patcher = Join-Path $root 'dist\PclPatcher.exe'
if (-not (Test-Path $patcher)) {
    throw "找不到 $patcher，请先运行 .\build.ps1"
}
if (-not (Test-Path $Pcl)) {
    throw "找不到 PCL 主程序：$Pcl"
}
$Pcl = (Resolve-Path $Pcl).Path

function Read-Identify {
    param([string]$Path)

    $fields = @{}
    & $patcher $Path --identify 2>$null | ForEach-Object {
        $pair = $_ -split "`t", 2
        if ($pair.Count -eq 2) { $fields[$pair[0]] = $pair[1] }
    }
    return $fields
}

$identity = Read-Identify -Path $Pcl
$build = if ($identity['build']) { $identity['build'] } else { '未知构建' }
$match = if ($identity['match']) { $identity['match'] } else { 'unknown' }
Write-Host "构建：$build" -ForegroundColor Cyan
Write-Host "匹配：$match（profile：$($identity['profile'])）" -ForegroundColor Cyan
Write-Host "哈希：$($identity['sha256'])"

$patchArgs = @($Pcl)
if ($DownloadOnly) { $patchArgs += '--download-only' }
if (-not $KeepUpdate) { $patchArgs += '--no-update' }

# 1) 先试运行。unknown 构建在这里会失败，错误信息里带 sha256 与支持列表。
Write-Host ''
Write-Host '== 试运行 ==' -ForegroundColor Cyan
$dryArgs = $patchArgs + '--dry-run'
& $patcher @dryArgs
$dryExit = $LASTEXITCODE

if ($dryExit -ne 0) {
    if (-not $Adopt) {
        Write-Host ''
        Write-Host '该构建无法按现有 profile 适配。' -ForegroundColor Yellow
        Write-Host '若确认是官方 PCL，可收录后重试：' -ForegroundColor Yellow
        Write-Host "  pwsh .\scripts\adapt_new_pcl.ps1 `"$Pcl`" -Adopt" -ForegroundColor Yellow
        exit $dryExit
    }

    Write-Host ''
    Write-Host '== 收录新构建 ==' -ForegroundColor Cyan
    & $patcher $Pcl --adopt --save
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Write-Host ''
    Write-Host '== 重新试运行 ==' -ForegroundColor Cyan
    & $patcher @dryArgs
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if ($DryRun) {
    Write-Host ''
    Write-Host '只探测：未写入文件。去掉 -DryRun 即可原地补丁。' -ForegroundColor Green
    exit 0
}

# 2) 真正写盘。补丁器自己会备份 .bak、结束正在运行的 PCL、写盘后重新加载校验。
Write-Host ''
Write-Host '== 应用补丁 ==' -ForegroundColor Cyan
& $patcher @patchArgs
exit $LASTEXITCODE
