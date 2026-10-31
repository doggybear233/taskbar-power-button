# 截图小工具（DPI 感知，输出物理像素）
# 用法: powershell -File shot.ps1 -Out <png> [-X x -Y y -W w -H h]
param(
    [Parameter(Mandatory=$true)][string]$Out,
    [int]$X = 0, [int]$Y = 0, [int]$W = 0, [int]$H = 0
)

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class ShotDpi {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
"@
[ShotDpi]::SetProcessDPIAware() | Out-Null

Add-Type -AssemblyName System.Windows.Forms, System.Drawing

$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
if ($W -le 0) { $W = $screen.Width }
if ($H -le 0) { $H = $screen.Height }

$bmp = New-Object System.Drawing.Bitmap($W, $H)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen((New-Object System.Drawing.Point($X, $Y)), [System.Drawing.Point]::Empty, (New-Object System.Drawing.Size($W, $H)))
$g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

Write-Host ("saved {0} ({1}x{2} from {3},{4})" -f $Out, $W, $H, $X, $Y)
