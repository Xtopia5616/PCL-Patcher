<#
.SYNOPSIS
    截取 PCL Patcher GUI 主界面，输出 README 用的界面截图。

.DESCRIPTION
    启动 dist/PclPatcher.Gui.Net48.exe（或指定 exe），等窗口渲染完成后用
    PrintWindow(PW_RENDERFULLCONTENT) 取窗口位图，写回 assets/screenshot/gui.png。
    截图前必须切到 Per-Monitor V2 DPI 感知，否则高缩放下拿到的是被裁切、放大的画面。

    注意：GUI 现在带 requireAdministrator 清单，未提权的 pwsh 启动它会被 UAC 挡住，
    脚本会一直等到 40 秒超时。请在管理员终端里运行本脚本；或者先构建一份无提权副本
    （dotnet build -p:PclPatcherNoElevation=true），再用 -Exe 指向它。

.PARAMETER PclPath
    传给 GUI 的 PCL 主程序路径；省略时由 GUI 自己查找。

.EXAMPLE
    pwsh .\assets\screenshot\capture.ps1
#>
param(
    [string]$Exe = "",
    [string]$Out = "",
    [string]$PclPath = "",
    [int]$WaitSeconds = 7
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win32Capture
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint flags);
}
"@

# PER_MONITOR_AWARE_V2。必须在创建任何窗口前调用。
[Win32Capture]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if ([string]::IsNullOrWhiteSpace($Exe)) {
    $Exe = Join-Path $repoRoot "dist\PclPatcher.Gui.Net48.exe"
}
if ([string]::IsNullOrWhiteSpace($Out)) {
    $Out = Join-Path $PSScriptRoot "gui.png"
}

$exePath = (Resolve-Path $Exe).Path
$outPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Out)
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $outPath) | Out-Null

$proc = if ([string]::IsNullOrWhiteSpace($PclPath)) {
    Start-Process -FilePath $exePath -PassThru
} else {
    Start-Process -FilePath $exePath -ArgumentList ('"' + $PclPath + '"') -PassThru
}

try {
    $deadline = (Get-Date).AddSeconds(40)
    while ($proc.MainWindowHandle -eq 0 -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 300
        $proc.Refresh()
    }
    if ($proc.MainWindowHandle -eq 0) { throw "GUI 窗口没有出现（检查 PCL 路径与 .NET Framework 4.8）" }

    # 给版本探测、主题与字体加载留出时间；窗口有淡入动画。
    Start-Sleep -Seconds $WaitSeconds
    $proc.Refresh()
    $hwnd = $proc.MainWindowHandle
    [Win32Capture]::ShowWindow($hwnd, 9) | Out-Null
    [Win32Capture]::SetWindowPos($hwnd, [IntPtr](-1), 0, 0, 0, 0, 0x0003 -bor 0x0040) | Out-Null
    [Win32Capture]::SetForegroundWindow($hwnd) | Out-Null
    Start-Sleep -Milliseconds 900

    $rect = New-Object Win32Capture+RECT
    [Win32Capture]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    if ($width -lt 200 -or $height -lt 200) { throw "窗口尺寸异常：${width}x${height}" }

    $bitmap = New-Object System.Drawing.Bitmap($width, $height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    $ok = [Win32Capture]::PrintWindow($hwnd, $hdc, 2)
    $graphics.ReleaseHdc($hdc)
    if (-not $ok) {
        # 提权窗口 / 被完全遮挡时 PrintWindow 会失败；窗口已置顶，直接抓屏。
        $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0,
            (New-Object System.Drawing.Size($width, $height)))
        Write-Host "PrintWindow 不可用，改用屏幕抓取"
    }
    $graphics.Dispose()

    $bitmap.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    [Win32Capture]::SetWindowPos($hwnd, [IntPtr](-2), 0, 0, 0, 0, 0x0003 -bor 0x0040) | Out-Null
    Write-Host ("saved {0} ({1}x{2})" -f $outPath, $width, $height)
}
finally {
    if (-not $proc.HasExited) { $proc.CloseMainWindow() | Out-Null; Start-Sleep -Seconds 2 }
    if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force }
}
