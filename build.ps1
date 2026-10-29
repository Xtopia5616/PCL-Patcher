#requires -Version 7.0
<#
.SYNOPSIS
    Builds the two deliverables: the self-contained patch tool and the net48 GUI.

.DESCRIPTION
    Output (dist/):
      PclPatcher.exe             self-contained dnlib patcher (full build)
      PclPatcher.Gui.Net48.exe   single-file GUI with the patcher embedded

    The GUI compiles against the user's own PCL installation for PCL's WPF
    controls. PCL itself is never copied into the output.

.PARAMETER PclLauncherPath
    Path to Plain Craft Launcher 2.exe. Defaults to the project default
    (D:\Minecraft\Plain Craft Launcher 2.exe).
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$PclLauncherPath
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

function Assert-NativeExit {
    param([string]$Step)

    if ($LASTEXITCODE -ne 0) {
        throw "$Step 失败，退出码 $LASTEXITCODE"
    }
}

$patcherProject = Join-Path $root 'src\PclPatcher\PclPatcher.csproj'
$guiProject = Join-Path $root 'src\PclPatcher.Gui.Net48\PclPatcher.Gui.Net48.csproj'
$dist = Join-Path $root 'dist'

Write-Host '== 1/3 构建 patcher（托管副本 + 自包含 single-file） ==' -ForegroundColor Cyan
dotnet build $patcherProject -c $Configuration --nologo
Assert-NativeExit '构建 patcher'

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
dotnet publish $patcherProject -c $Configuration -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    --nologo -o $dist
Assert-NativeExit '发布 patcher'

Write-Host '== 2/3 构建 net48 GUI（内嵌 patcher 与 PclTheme.xaml） ==' -ForegroundColor Cyan
$guiArguments = @($guiProject, '-c', $Configuration, '--nologo')
if ($PclLauncherPath) { $guiArguments += "-p:PclLauncherPath=$PclLauncherPath" }
dotnet build @guiArguments
Assert-NativeExit '构建 GUI'

Write-Host '== 3/3 收集产物 ==' -ForegroundColor Cyan
$guiExe = Join-Path $root "src\PclPatcher.Gui.Net48\bin\$Configuration\net48\PclPatcher.Gui.Net48.exe"
if (-not (Test-Path $guiExe)) { throw "GUI 构建产物不存在：$guiExe" }
Copy-Item $guiExe $dist -Force
Remove-Item (Join-Path $dist '*.pdb') -Force -ErrorAction SilentlyContinue

Write-Host ''
Get-ChildItem $dist -File | ForEach-Object {
    Write-Host ('  {0,-28} {1,7:F1} MB' -f $_.Name, ($_.Length / 1MB)) -ForegroundColor Green
}
Write-Host ''
Write-Host 'done: dist/PclPatcher.exe（完整补丁工具）' -ForegroundColor Green
Write-Host 'done: dist/PclPatcher.Gui.Net48.exe（单文件 GUI，PCL 本体不随包分发）' -ForegroundColor Green
