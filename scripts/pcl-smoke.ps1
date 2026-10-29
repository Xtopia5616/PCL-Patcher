param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [Parameter(Mandatory = $true)][string]$Tag,
    [int]$WaitSeconds = 60,
    [switch]$KeepRunning
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\..\.trash\uia-lib.ps1"

$shotDir = Join-Path $PSScriptRoot '..\.test\shots'
New-Item -ItemType Directory -Force -Path $shotDir | Out-Null

Get-Process -Name 'Plain Craft Launcher 2' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800

$proc = Start-Process -FilePath $Exe -PassThru
Write-Host "started pid=$($proc.Id)"
for ($i = 0; $i -lt ($WaitSeconds * 4) -and $proc.MainWindowHandle -eq 0 -and -not $proc.HasExited; $i++) {
    Start-Sleep -Milliseconds 250
    $proc.Refresh()
}
Write-Host "hwnd=$($proc.MainWindowHandle) exited=$($proc.HasExited)"
if ($proc.HasExited) { throw 'PCL exited before showing a window' }

[Ui]::SetForegroundWindow([IntPtr]$proc.MainWindowHandle) | Out-Null
Start-Sleep -Seconds 5

$rect = New-Object Ui+RECT
[void][Ui]::GetWindowRect([IntPtr]$proc.MainWindowHandle, [ref]$rect)
Write-Host "rect=$($rect.Left),$($rect.Top) $($rect.Right - $rect.Left)x$($rect.Bottom - $rect.Top)"

$shotPath = Join-Path $shotDir "$Tag-home.png"
Add-Type -AssemblyName System.Windows.Forms
$bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bmp = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($bounds.X, $bounds.Y, 0, 0, $bmp.Size)
$bmp.Save($shotPath, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Host "saved $shotPath"

$win = Get-AppWindow $proc
$items = Get-Items $win | Where-Object { $_.Name }
"--- visible UIA names ($($items.Count)) ---"
$items | ForEach-Object { "$($_.X),$($_.Y) $($_.W)x$($_.H)`t$($_.Name)" } | Select-Object -First 80

if (-not $KeepRunning) {
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    Write-Host 'stopped'
}
