param(
    [string]$Source,
    [Parameter(Mandatory = $true)][string]$Tag,
    [string]$SetSource,
    [switch]$OpenCombo
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\..\.trash\uia-lib.ps1"
Add-Type -AssemblyName System.Windows.Forms

Get-Process -Name 'PclPatcher.Gui.Net48' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 600

$exe = (Resolve-Path "$PSScriptRoot\..\.test\gui-verify\PclPatcher.Gui.Net48.exe").Path
$launchArgs = @()
if ($Source) { $launchArgs = @("`"$Source`"") }
$p = Start-Process -FilePath $exe -ArgumentList $launchArgs -PassThru
for ($i = 0; $i -lt 160 -and $p.MainWindowHandle -eq 0 -and -not $p.HasExited; $i++) {
    Start-Sleep -Milliseconds 250
    $p.Refresh()
}
if ($p.HasExited) { throw "GUI exited (code $($p.ExitCode))" }

$hwnd = [IntPtr]$p.MainWindowHandle
$rect = New-Object Ui+RECT
[void][Ui]::GetWindowRect($hwnd, [ref]$rect)
if ($rect.Right -le $rect.Left) { $rect.Left = 553; $rect.Top = 210 }
Write-Host "window $($rect.Left),$($rect.Top) $($rect.Right - $rect.Left)x$($rect.Bottom - $rect.Top)"

[Ui]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Seconds 3

if ($SetSource) {
    # source text box sits at window-relative (672, 343) on the 200% layout
    [Ui]::ClickAt($rect.Left + 672, $rect.Top + 343)
    Start-Sleep -Milliseconds 400
    Set-Clipboard -Value $SetSource
    [System.Windows.Forms.SendKeys]::SendWait('^a')
    Start-Sleep -Milliseconds 150
    [System.Windows.Forms.SendKeys]::SendWait('^v')
    Start-Sleep -Milliseconds 400
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
    Start-Sleep -Seconds 4
}

if ($OpenCombo) {
    # combo box sits at window-relative (1117, 267); click the chevron on the right
    [Ui]::ClickAt($rect.Left + 1117, $rect.Top + 267)
    Start-Sleep -Seconds 1
}

$b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($b.X, $b.Y, 0, 0, $bmp.Size)
$path = Join-Path $PSScriptRoot "..\.test\shots\$Tag.png"
$bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
"saved $path ($($b.Width)x$($b.Height))"

Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
