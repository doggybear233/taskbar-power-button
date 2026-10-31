#requires -Version 5.1
<#
    install.ps1 - 把任务栏上的 shutdown 快捷方式换成一个可撤销的关机按钮。

    做的事：
      1. 生成电源图标 power.ico
      2. 用系统自带的 csc.exe 把 src\PowerButton.cs 编译成 %LOCALAPPDATA%\PowerButton\PowerButton.exe
      3. 备份原始 shutdown.lnk（只备份一次，不会被覆盖）
      4. 原地改写 shutdown.lnk 的 Target / 参数 / 图标 / 注释（保留同一个文件 = 保留任务栏固定位置）
      5. 注册右键动词「电源菜单」，并刷新图标缓存

    只需普通用户权限。可反复运行（升级时直接重跑）。
#>

$ErrorActionPreference = 'Stop'

$root    = Split-Path -Parent $MyInvocation.MyCommand.Path
$source  = Join-Path $root 'src\PowerButton.cs'
$manifest = Join-Path $root 'src\app.manifest'
$outDir  = Join-Path $env:LOCALAPPDATA 'PowerButton'
$backup  = Join-Path $outDir 'backup'
$exe     = Join-Path $outDir 'PowerButton.exe'
$icon    = Join-Path $outDir 'power.ico'
$csc     = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'

$linkDir = Join-Path $env:APPDATA 'Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar'
$link    = Join-Path $linkDir 'shutdown.lnk'

function Write-Step([string]$text) {
    Write-Host ("==> " + $text) -ForegroundColor Cyan
}

# ---------------------------------------------------------------- 前置检查
if (-not (Test-Path $source)) { throw "找不到源码：$source" }
if (-not (Test-Path $manifest)) { throw "找不到清单文件：$manifest" }
if (-not (Test-Path $csc))    { throw "找不到 C# 编译器：$csc" }

New-Item -ItemType Directory -Force -Path $outDir | Out-Null
New-Item -ItemType Directory -Force -Path $backup | Out-Null

# ---------------------------------------------------------------- 图标
Write-Step "安装图标"
$iconSrc = Join-Path $root 'icons\power.ico'
if (-not (Test-Path $iconSrc)) {
    $maker = Join-Path $root 'tools\make-icon.ps1'
    if (-not (Test-Path $maker)) { throw "找不到图标：$iconSrc" }
    Write-Host "    没有现成图标，正在生成（需要 Chrome）..."
    & $maker
    if (-not (Test-Path $iconSrc)) { throw "图标生成失败：$iconSrc" }
}
Copy-Item -Path $iconSrc -Destination $icon -Force
Write-Host ("    " + $icon)

# ---------------------------------------------------------------- 编译
Write-Step "编译 PowerButton.exe"
Get-Process -Name 'PowerButton' -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "    先结束正在运行的实例 PID $($_.Id)"
    $_.Kill()
    $_.WaitForExit(3000)
}

$cscArgs = @(
    '/nologo'
    '/target:winexe'
    '/optimize+'
    '/codepage:65001'
    "/win32manifest:$manifest"
    "/win32icon:$icon"
    "/out:$exe"
    '/reference:System.dll'
    '/reference:System.Drawing.dll'
    '/reference:System.Windows.Forms.dll'
    $source
)

$compile = & $csc @cscArgs 2>&1
$compile | Where-Object { $_ -match '\S' } | ForEach-Object { Write-Host ("    " + $_) }

if ($LASTEXITCODE -ne 0) { throw "编译失败（退出码 $LASTEXITCODE）" }
if (-not (Test-Path $exe)) { throw "编译没有产出 $exe" }
Write-Host ("    " + $exe)

# 悬停说明和右键动词名都由程序按当前界面语言生成（中文 / 日本語 / English）
$q = [char]34
$descFile = Join-Path $env:TEMP 'powerbutton-desc.txt'
$descProc = Start-Process -FilePath $exe -Wait -PassThru -ArgumentList @('--label-out', 'lnk.description', ($q + $descFile + $q))
if ($descProc.ExitCode -ne 0 -or -not (Test-Path $descFile)) { throw "生成快捷方式备注失败（退出码 $($descProc.ExitCode)）" }
$descText = [System.IO.File]::ReadAllText($descFile, [System.Text.Encoding]::UTF8).TrimEnd("`r", "`n")

$verbFile = Join-Path $env:TEMP 'powerbutton-verb.txt'
$verbProc = Start-Process -FilePath $exe -Wait -PassThru -ArgumentList @('--label-out', 'menu.verb', ($q + $verbFile + $q))
if ($verbProc.ExitCode -ne 0 -or -not (Test-Path $verbFile)) { throw "生成右键菜单名失败（退出码 $($verbProc.ExitCode)）" }
$verbText = [System.IO.File]::ReadAllText($verbFile, [System.Text.Encoding]::UTF8).TrimEnd("`r", "`n")
# ---------------------------------------------------------------- 备份
$backupFile = Join-Path $backup 'shutdown.lnk.bak'
if ((Test-Path $link) -and -not (Test-Path $backupFile)) {
    Copy-Item -LiteralPath $link -Destination $backupFile -Force
    Write-Step "已备份原始快捷方式"
    Write-Host ("    " + $backupFile)
}
elseif (Test-Path $backupFile) {
    Write-Step "备份已存在，保留最早的那份"
}

# ---------------------------------------------------------------- 改写快捷方式
Write-Step "改写 shutdown.lnk"
if (Test-Path $link) {
    $shell = New-Object -ComObject WScript.Shell
    $sc = $shell.CreateShortcut($link)
    $sc.TargetPath        = $exe
    $sc.Arguments         = ''
    $sc.WorkingDirectory  = $outDir
    $sc.IconLocation      = ($exe + ',0')
    $sc.Description       = $descText
    $sc.WindowStyle       = 1
    $sc.Save()
    # 立刻释放 COM 引用，否则 .lnk 仍被这个进程占着，后面改写备注会静默失败
    [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($sc)
    [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($shell)
    Remove-Variable sc, shell -ErrorAction SilentlyContinue
}
else {
    Write-Warning "任务栏上没有找到 shutdown.lnk，跳过改写。"
    Write-Warning "请把 $exe 手动固定到任务栏。"
}

# WScript.Shell 会按系统 ANSI 代码页（本机 CP932）转码，中文被写成 '?'。
# 改用程序自己的 IShellLinkW 路径重写备注，保证悬停提示是完整中文。
if (Test-Path $link) {
    # 路径要显式加引号：Start-Process 不会替我们转义空格（"Quick Launch" 就带空格）
    $fix = Start-Process -FilePath $exe -Wait -PassThru -ArgumentList @('--set-description', ($q + $link + $q), '--file', ($q + $descFile + $q))
    if ($fix.ExitCode -ne 0) { throw ("写入快捷方式备注失败（退出码 " + $fix.ExitCode + "）") }
}
# ---------------------------------------------------------------- 右键动词
Write-Step "注册外壳动词"
$verb = 'HKCU:\Software\Classes\Applications\PowerButton.exe\shell\menu'
New-Item -Path $verb -Force | Out-Null
Set-ItemProperty -Path $verb -Name '(Default)' -Value $verbText
Set-ItemProperty -Path $verb -Name 'Icon' -Value ($exe + ',0')
New-Item -Path ($verb + '\command') -Force | Out-Null
Set-ItemProperty -Path ($verb + '\command') -Name '(Default)' -Value ('"' + $exe + '" --menu')

# ---------------------------------------------------------------- 刷新缓存
Write-Step "刷新图标缓存"
$ie4uinit = Join-Path $env:WINDIR 'System32\ie4uinit.exe'
if (Test-Path $ie4uinit) {
    & $ie4uinit -show | Out-Null
    & $ie4uinit -ClearIconCache 2>$null | Out-Null
}

# ---------------------------------------------------------------- 结果
Write-Step "完成"
if (Test-Path $link) {
    $verify = (New-Object -ComObject WScript.Shell).CreateShortcut($link)
    Write-Host ("    目标: " + $verify.TargetPath)
    Write-Host ("    参数: " + $verify.Arguments)
    Write-Host ("    图标: " + $verify.IconLocation)
}
Write-Host ""
Write-Host "左键单击：5 秒倒计时关机，期间再点一次任务栏按钮即可取消。" -ForegroundColor Gray
Write-Host "Shift+左键，或倒计时卡片上的「电源菜单」按钮：睡眠/重启/关机/注销/锁定/关屏/定时/取消。" -ForegroundColor Gray
Write-Host "如果任务栏图标没有立刻更新，注销或重启一次资源管理器即可。" -ForegroundColor Gray
