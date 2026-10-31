#requires -Version 5.1
<#
    uninstall.ps1 - 把任务栏按钮还原成原始的 shutdown.exe 快捷方式，并清除安装的程序。
#>

$ErrorActionPreference = 'Stop'

$outDir  = Join-Path $env:LOCALAPPDATA 'PowerButton'
$backup  = Join-Path $outDir 'backup\shutdown.lnk.bak'
$linkDir = Join-Path $env:APPDATA 'Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar'
$link    = Join-Path $linkDir 'shutdown.lnk'
$verb    = 'HKCU:\Software\Classes\Applications\PowerButton.exe'

function Write-Step([string]$text) {
    Write-Host ("==> " + $text) -ForegroundColor Cyan
}

Write-Step "结束正在运行的实例"
Get-Process -Name 'PowerButton' -ErrorAction SilentlyContinue | ForEach-Object {
    try { $_.Kill(); $_.WaitForExit(3000) } catch { }
}

Write-Step "还原 shutdown.lnk"
if (Test-Path $backup) {
    Copy-Item -LiteralPath $backup -Destination $link -Force
    $v = (New-Object -ComObject WScript.Shell).CreateShortcut($link)
    Write-Host ("    目标: " + $v.TargetPath)
    Write-Host ("    参数: " + $v.Arguments)
}
else {
    Write-Warning "没有找到备份，无法自动还原：$backup"
    Write-Warning "可以手动把快捷方式指向 shutdown.exe，参数 -s -t 0。"
}

Write-Step "移除右键菜单命令"
if (Test-Path $verb) { Remove-Item -Path $verb -Recurse -Force }

Write-Step "刷新图标缓存"
$ie4uinit = Join-Path $env:WINDIR 'System32\ie4uinit.exe'
if (Test-Path $ie4uinit) { & $ie4uinit -show | Out-Null }

Write-Step "删除安装目录"
if (Test-Path $outDir) { Remove-Item -Path $outDir -Recurse -Force }

Write-Step "完成"
Write-Host "任务栏按钮已还原为原来的关机快捷方式（立即关机）。" -ForegroundColor Gray
