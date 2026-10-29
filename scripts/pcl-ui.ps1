param(
    [string]$Name = 'Plain Craft Launcher 2',
    [string]$Click,
    [string]$Shot,
    [int]$Dump = 40,
    [int]$WaitMs = 1500
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\..\.trash\uia-lib.ps1"

$proc = Get-Process -Name $Name -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) { throw "process $Name not running" }

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)
$windows = @($root.FindAll([System.Windows.Automation.TreeScope]::Children, $cond))
if ($windows.Count -eq 0) { throw 'no UIA window' }
$win = $windows[0]

[Ui]::SetForegroundWindow([IntPtr]$proc.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 400

if ($Click) {
    $target = $null
    foreach ($w in $windows) {
        $target = Get-Items $w | Where-Object { $_.Name -eq $Click } | Select-Object -First 1
        if ($target) { break }
    }
    if (-not $target) { throw "element not found: $Click" }
    $cx = $target.X + [int]($target.W / 2)
    $cy = $target.Y + [int]($target.H / 2)
    [Ui]::ClickAt($cx, $cy)
    Write-Host "clicked '$Click' at $cx,$cy"
    Start-Sleep -Milliseconds $WaitMs
}

if ($Shot) {
    $dir = Join-Path $PSScriptRoot '..\.test\shots'
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $path = Join-Path $dir "$Shot.png"
    Add-Type -AssemblyName System.Windows.Forms
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bmp = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($bounds.X, $bounds.Y, 0, 0, $bmp.Size)
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Write-Host "saved $path"
}

if ($Dump -gt 0) {
    $after = Get-Items $win | Where-Object { $_.Name }
    Write-Host "--- items ($($after.Count)) ---"
    $after | Select-Object -First $Dump | ForEach-Object { "$($_.X),$($_.Y) $($_.W)x$($_.H)`t$($_.Name)" }
}
