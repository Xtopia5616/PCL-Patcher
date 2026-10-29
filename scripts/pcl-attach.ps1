param([string]$Name = 'Plain Craft Launcher 2', [int]$Max = 60)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\..\.trash\uia-lib.ps1"

$proc = Get-Process -Name $Name -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) { throw "process $Name not running" }
Write-Host "pid=$($proc.Id) hwnd=$($proc.MainWindowHandle) title='$($proc.MainWindowTitle)'"

$wins = Get-TopWindows $proc.Id
foreach ($w in $wins) { Write-Host ("win: '{0}' {1} hwnd={2} {3},{4} {5}x{6}" -f $w.Name, $w.Type, $w.Handle, $w.X, $w.Y, $w.W, $w.H) }

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Host 'UIA: no top-level window for pid'; exit 1 }

$items = Get-Items $win | Where-Object { $_.Name }
Write-Host "--- items ($($items.Count)) ---"
$items | Select-Object -First $Max | ForEach-Object { "$($_.X),$($_.Y) $($_.W)x$($_.H)`t$($_.Name)" }
