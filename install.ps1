#requires -Version 5.1
<#
    install.ps1 - 把任务栏上的 shutdown 快捷方式换成一个可撤销的关机按钮。
    install.ps1 - replaces the pinned "shutdown" shortcut with a cancellable power button.
    install.ps1 - タスクバーの shutdown ショートカットを「取り消せる電源ボタン」に置き換えます。

    做的事 / what it does:
      1. 复制电源图标 power.ico
      2. 用系统自带 csc.exe 把 src\PowerButton.cs 编译到 %LOCALAPPDATA%\PowerButton\
      3. 备份原始 .lnk（只备份一次）
      4. 原地改写该 .lnk 的 Target / 参数 / 图标 / 备注（同一个文件 = 保留任务栏固定位置）
      5. 注册外壳动词，并刷新图标缓存

    只需普通用户权限；可反复运行（升级时直接重跑）。
    No administrator rights; safe to re-run (that is how you upgrade).
#>

param(
    # 明确指定要替换的快捷方式；默认自动找任务栏上指向 shutdown.exe 的那一个。
    [string] $LnkPath,
    # 允许覆盖一个看起来不像是 shutdown 的快捷方式。
    [switch] $Force
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

# ---------------------------------------------------------------- 语言
# 用系统显示语言，而不是进程的 CurrentUICulture：某些启动器
# （包括从 PowerShell 里起的子进程）会把 UI 语言误报成 en-US。
$script:ui = $null
try { $script:ui = ([System.Globalization.CultureInfo]::InstalledUICulture).TwoLetterISOLanguageName } catch { }
if ($env:POWERBUTTON_LANG -in @('zh','ja','en')) { $script:ui = $env:POWERBUTTON_LANG }
if (-not $script:ui) {
    try { $script:ui = ([System.Globalization.CultureInfo]::CurrentUICulture).TwoLetterISOLanguageName } catch { $script:ui = 'en' }
}
if ($script:ui -notin @('zh', 'ja')) { $script:ui = 'en' }

$M = @{
    zh = @{
        stepIcon     = '安装图标'
        stepCompile  = '编译 PowerButton.exe'
        stepLink     = '查找任务栏快捷方式'
        stepBackup   = '备份原始快捷方式'
        stepRewrite  = '改写快捷方式'
        stepVerb     = '注册外壳动词'
        stepCache    = '刷新图标缓存'
        stepDone     = '完成'
        killRunning  = '先结束正在运行的实例 PID {0}'
        noLink       = '没找到任务栏上指向 shutdown.exe 的快捷方式，程序已安装但没有替换任何按钮。'
        noLinkHint   = '请把 {0} 手动固定到任务栏，或右键运行：install.ps1 -LnkPath "<你的快捷方式.lnk>"'
        notShutdown  = '这个快捷方式指向 {0}，看起来不是关机按钮；为安全起见没有改写。加 -Force 可强制。'
        backupDone   = '已备份到 {0}'
        backupKept   = '备份已存在，保留最早的那份'
        target       = '目标'
        args         = '参数'
        icon         = '图标'
        usage1       = '左键单击：倒计时关机，期间再点一次任务栏按钮即可取消。'
        usage2       = '双击、Shift+左键，或倒计时卡片上的「电源菜单」按钮：打开电源菜单。'
        usage3       = '如果任务栏图标没有立刻更新，注销或重启一次资源管理器即可。'
        errSource    = '找不到源码：{0}'
        errManifest  = '找不到清单文件：{0}'
        errCompiler  = '找不到 C# 编译器（.NET Framework 4.x 的 csc.exe）。请先在「启用或关闭 Windows 功能」里勾选 .NET Framework 4.8。'
        errIcon      = '找不到图标：{0}'
        errCompile   = '编译失败（退出码 {0}）'
        errNoExe     = '编译没有产出 {0}'
        errDesc      = '生成快捷方式备注失败（退出码 {0}）'
        errVerb      = '生成外壳动词名失败（退出码 {0}）'
        errSetDesc   = '写入快捷方式备注失败（退出码 {0}）'
        errLnkMissing = '找不到指定的快捷方式：{0}'
    }
    ja = @{
        stepIcon     = 'アイコンを配置'
        stepCompile  = 'PowerButton.exe をビルド'
        stepLink     = 'タスクバーのショートカットを検索'
        stepBackup   = '元のショートカットをバックアップ'
        stepRewrite  = 'ショートカットを書き換え'
        stepVerb     = 'シェル動詞を登録'
        stepCache    = 'アイコンキャッシュを更新'
        stepDone     = '完了'
        killRunning  = '実行中のインスタンス PID {0} を終了します'
        noLink       = 'shutdown.exe を指すタスクバーのショートカットが見つかりませんでした。プログラムは入りましたが、ボタンの置き換えは行っていません。'
        noLinkHint   = '{0} を手動でタスクバーにピン留めするか、install.ps1 -LnkPath "<ショートカット.lnk>" を実行してください'
        notShutdown  = 'このショートカットは {0} を指しており、電源ボタンではなさそうです。安全のため書き換えませんでした。-Force で強行できます。'
        backupDone   = '{0} にバックアップしました'
        backupKept   = 'バックアップは既にあります。最初のものを残します'
        target       = 'リンク先'
        args         = '引数'
        icon         = 'アイコン'
        usage1       = '左クリック：カウントダウンしてシャットダウン。その間にもう一度押すと中止できます。'
        usage2       = 'ダブルクリック、Shift+左クリック、またはカードの「電源メニュー」ボタンで電源メニューを開きます。'
        usage3       = 'タスクバーのアイコンがすぐ変わらない場合は、サインアウトするかエクスプローラーを再起動してください。'
        errSource    = 'ソースが見つかりません: {0}'
        errManifest  = 'マニフェストが見つかりません: {0}'
        errCompiler  = 'C# コンパイラー (.NET Framework 4.x の csc.exe) が見つかりません。「Windows の機能の有効化または無効化」で .NET Framework 4.8 を有効にしてください。'
        errIcon      = 'アイコンが見つかりません: {0}'
        errCompile   = 'ビルドに失敗しました (終了コード {0})'
        errNoExe     = '{0} が生成されませんでした'
        errDesc      = 'ショートカットの説明文を生成できませんでした (終了コード {0})'
        errVerb      = 'シェル動詞名を生成できませんでした (終了コード {0})'
        errSetDesc   = 'ショートカットの説明文を書き込めませんでした (終了コード {0})'
        errLnkMissing = '指定されたショートカットが見つかりません: {0}'
    }
    en = @{
        stepIcon     = 'Installing the icon'
        stepCompile  = 'Compiling PowerButton.exe'
        stepLink     = 'Looking for the taskbar shortcut'
        stepBackup   = 'Backing up the original shortcut'
        stepRewrite  = 'Rewriting the shortcut'
        stepVerb     = 'Registering the shell verb'
        stepCache    = 'Refreshing the icon cache'
        stepDone     = 'Done'
        killRunning  = 'Stopping the running instance, PID {0}'
        noLink       = 'No taskbar shortcut pointing at shutdown.exe was found. The program is installed, but no button was replaced.'
        noLinkHint   = 'Pin {0} to the taskbar yourself, or run install.ps1 -LnkPath "<your shortcut.lnk>"'
        notShutdown  = 'That shortcut points at {0}, which does not look like the shutdown button, so it was left alone. Use -Force to override.'
        backupDone   = 'Saved to {0}'
        backupKept   = 'A backup already exists; the first one is kept'
        target       = 'Target'
        args         = 'Arguments'
        icon         = 'Icon'
        usage1       = 'Left click: counts down, then shuts down. Clicking the taskbar button again during the countdown cancels it.'
        usage2       = 'Double-click, Shift+click, or the Power menu button on the card to open the power menu.'
        usage3       = 'If the taskbar icon does not change immediately, sign out and back in (Explorer cached it).'
        errSource    = 'Source file not found: {0}'
        errManifest  = 'Manifest not found: {0}'
        errCompiler  = 'No C# compiler found (csc.exe from .NET Framework 4.x). Enable .NET Framework 4.8 under "Turn Windows features on or off".'
        errIcon      = 'Icon not found: {0}'
        errCompile   = 'Compilation failed (exit code {0})'
        errNoExe     = 'Compilation produced no {0}'
        errDesc      = 'Could not build the shortcut description (exit code {0})'
        errVerb      = 'Could not build the shell verb name (exit code {0})'
        errSetDesc   = 'Could not write the shortcut description (exit code {0})'
        errLnkMissing = 'Shortcut not found: {0}'
    }
}

$L = $M[$script:ui]

function T([string] $key) { return $L[$key] }
function Tf([string] $key, [object[]] $values) { return ([string]$L[$key] -f $values) }
function Write-Step([string] $text) { Write-Host ("==> " + $text) -ForegroundColor Cyan }

trap {
    Write-Host ('x ' + $_.Exception.Message) -ForegroundColor Red
    exit 1
}
function Fail([string] $message) { throw $message }

# ---------------------------------------------------------------- 路径
$root = $PSScriptRoot
if (-not $root) { $root = Split-Path -Parent $MyInvocation.MyCommand.Path }

$source   = Join-Path $root 'src\PowerButton.cs'
$manifest = Join-Path $root 'src\app.manifest'
$outDir   = Join-Path $env:LOCALAPPDATA 'PowerButton'
$backup   = Join-Path $outDir 'backup'
$exe      = Join-Path $outDir 'PowerButton.exe'
$icon     = Join-Path $outDir 'power.ico'
$linkDir  = Join-Path $env:APPDATA 'Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar'

function Find-Compiler {
    # 64 位系统优先用 Framework64；32 位系统只有 Framework。
    # ARM64 上 Framework64 里的 x64 csc 由系统模拟运行，同样可用；万一不行再退到纯 IL 的那个。
    $candidates = @(
        (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
        (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'),
        (Join-Path $env:ProgramFiles 'Microsoft.NET\Framework64\v4.0.30319\csc.exe')
    )
    if (${env:ProgramFiles(x86)}) {
        $candidates += (Join-Path ${env:ProgramFiles(x86)} 'Microsoft.NET\Framework64\v4.0.30319\csc.exe')
    }
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) { return $candidate }
    }
    return $null
}

function Find-TaskbarLink {
    param([string] $Explicit)

    if ($Explicit) {
        if (-not (Test-Path -LiteralPath $Explicit)) { Fail (Tf 'errLnkMissing' @($Explicit)) }
        return (Get-Item -LiteralPath $Explicit)
    }

    if (-not (Test-Path -LiteralPath $linkDir)) { return $null }

    $exact = Join-Path $linkDir 'shutdown.lnk'
    if (Test-Path -LiteralPath $exact) { return (Get-Item -LiteralPath $exact) }

    # 有的系统把这条快捷方式改了名字（本地化、或用户自己改过），按目标找一遍。
    $shell = New-Object -ComObject WScript.Shell
    $found = $null
    try {
        foreach ($file in (Get-ChildItem -LiteralPath $linkDir -Filter '*.lnk' -File -ErrorAction SilentlyContinue)) {
            try {
                $shortcut = $shell.CreateShortcut($file.FullName)
                $target = $shortcut.TargetPath
                [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($shortcut)
                if ($target -and (Split-Path -Leaf $target) -ieq 'shutdown.exe') { $found = $file; break }
            }
            catch { }
        }
    }
    finally {
        try { [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($shell) } catch { }
    }
    return $found
}

# 下载来的 zip 会给文件打上「来自网络」标记，RemoteSigned 策略下脚本会被直接拒绝。
# 自己能跑到这里就说明已经过了执行策略，但顺手标记解锁，后面生成的快捷方式也不会被牵连。
try {
    Get-ChildItem -LiteralPath $root -Recurse -File -ErrorAction SilentlyContinue |
        ForEach-Object { try { Unblock-File -LiteralPath $_.FullName -ErrorAction SilentlyContinue } catch { } }
} catch { }

# ---------------------------------------------------------------- 前置检查
if (-not (Test-Path -LiteralPath $source))   { Fail (Tf 'errSource' @($source)) }
if (-not (Test-Path -LiteralPath $manifest)) { Fail (Tf 'errManifest' @($manifest)) }

$csc = Find-Compiler
if (-not $csc) { Fail (T 'errCompiler') }

New-Item -ItemType Directory -Force -Path $outDir | Out-Null
New-Item -ItemType Directory -Force -Path $backup | Out-Null

# ---------------------------------------------------------------- 图标
Write-Step (T 'stepIcon')
$iconSrc = Join-Path $root 'icons\power.ico'
if (-not (Test-Path -LiteralPath $iconSrc)) {
    $maker = Join-Path $root 'tools\make-icon.ps1'
    if (-not (Test-Path -LiteralPath $maker)) { Fail (Tf 'errIcon' @($iconSrc)) }
    & $maker
    if (-not (Test-Path -LiteralPath $iconSrc)) { Fail (Tf 'errIcon' @($iconSrc)) }
}
Copy-Item -LiteralPath $iconSrc -Destination $icon -Force
Write-Host ('    ' + $icon)

# ---------------------------------------------------------------- 编译
Write-Step (T 'stepCompile')
Get-Process -Name 'PowerButton' -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host ('    ' + (Tf 'killRunning' @($_.Id)))
    try { $_.Kill(); $_.WaitForExit(3000) } catch { }
    Start-Sleep -Milliseconds 120
}

$cscArgs = @(
    '/nologo'
    '/target:winexe'
    '/optimize+'
    '/codepage:65001'
    ('/win32manifest:' + $manifest)
    ('/win32icon:' + $icon)
    ('/out:' + $exe)
    '/reference:System.dll'
    '/reference:System.Drawing.dll'
    '/reference:System.Windows.Forms.dll'
    $source
)

$compile = & $csc $cscArgs 2>&1
$compile | Where-Object { $_ -match '\S' } | ForEach-Object { Write-Host ('    ' + $_) }
if ($LASTEXITCODE -ne 0) { Fail (Tf 'errCompile' @($LASTEXITCODE)) }
if (-not (Test-Path -LiteralPath $exe)) { Fail (Tf 'errNoExe' @($exe)) }
Write-Host ('    ' + $exe)

# 备注文本与动词名都交给程序按当前界面语言生成，脚本里不出现任何界面文案。
$q = [char]34
$descFile = Join-Path $env:TEMP 'powerbutton-desc.txt'
$descProc = Start-Process -FilePath $exe -Wait -PassThru -ArgumentList @('--label-out', 'lnk.description', ($q + $descFile + $q))
if ($descProc.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $descFile)) { Fail (Tf 'errDesc' @($descProc.ExitCode)) }
$descText = (Get-Content -LiteralPath $descFile -Raw -Encoding UTF8).Trim()

$verbFile = Join-Path $env:TEMP 'powerbutton-verb.txt'
$verbProc = Start-Process -FilePath $exe -Wait -PassThru -ArgumentList @('--label-out', 'menu.verb', ($q + $verbFile + $q))
if ($verbProc.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $verbFile)) { Fail (Tf 'errVerb' @($verbProc.ExitCode)) }
$verbText = (Get-Content -LiteralPath $verbFile -Raw -Encoding UTF8).Trim()

# ---------------------------------------------------------------- 快捷方式
Write-Step (T 'stepLink')
$link = $null
try { $link = Find-TaskbarLink -Explicit $LnkPath } catch { Fail $_.Exception.Message }

$rewrite = $true
if (-not $link) {
    Write-Warning (T 'noLink')
    Write-Warning (Tf 'noLinkHint' @($exe))
    $rewrite = $false
}
else {
    Write-Host ('    ' + $link.FullName)
    $probe = (New-Object -ComObject WScript.Shell).CreateShortcut($link.FullName)
    $currentTarget = $probe.TargetPath
    [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($probe)
    $isShutdown = $currentTarget -and (Split-Path -Leaf $currentTarget) -ieq 'shutdown.exe'
    $isOurs = $currentTarget -and ($currentTarget -ieq $exe)
    if (-not ($isShutdown -or $isOurs) -and -not $Force) {
        Write-Warning (Tf 'notShutdown' @($currentTarget))
        $rewrite = $false
    }
}

if ($rewrite) {
    # ------------------------------------------------------------ 备份
    $backupFile = Join-Path $backup 'shutdown.lnk.bak'
    if (-not (Test-Path -LiteralPath $backupFile)) {
        Write-Step (T 'stepBackup')
        Copy-Item -LiteralPath $link.FullName -Destination $backupFile -Force
        Write-Host ('    ' + (Tf 'backupDone' @($backupFile)))
    }
    else {
        Write-Step (T 'stepBackup')
        Write-Host ('    ' + (T 'backupKept'))
    }
    # 记住当初改的是哪一个文件，卸载时才能还原到同一个位置
    Set-Content -LiteralPath (Join-Path $backup 'link.txt') -Value $link.FullName -Encoding UTF8

    # ------------------------------------------------------------ 改写
    Write-Step (T 'stepRewrite')
    $shell = New-Object -ComObject WScript.Shell
    $sc = $shell.CreateShortcut($link.FullName)
    $sc.TargetPath       = $exe
    $sc.Arguments        = ''
    $sc.WorkingDirectory = $outDir
    $sc.IconLocation     = ($exe + ',0')
    $sc.Description      = $descText
    $sc.WindowStyle      = 1
    $sc.Save()
    [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($sc)
    [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($shell)
    Remove-Variable sc, shell -ErrorAction SilentlyContinue

    # WScript.Shell 按系统 ANSI 代码页写 .lnk，非该代码页的字符会变成 '?'。
    # 备注改由程序走 IShellLinkW/IPersistFile 重写，纯 UTF-16。
    $fixArgs = @('--set-description', ($q + $link.FullName + $q), ($q + $descText + $q))
    $fix = Start-Process -FilePath $exe -Wait -PassThru -ArgumentList $fixArgs
    if ($fix.ExitCode -ne 0) { Fail (Tf 'errSetDesc' @($fix.ExitCode)) }
}

# ---------------------------------------------------------------- 外壳动词
Write-Step (T 'stepVerb')
$verb = 'HKCU:\Software\Classes\Applications\PowerButton.exe\shell\menu'
New-Item -Path $verb -Force | Out-Null
Set-ItemProperty -Path $verb -Name '(Default)' -Value $verbText
Set-ItemProperty -Path $verb -Name 'Icon' -Value ($exe + ',0')
New-Item -Path ($verb + '\command') -Force | Out-Null
Set-ItemProperty -Path ($verb + '\command') -Name '(Default)' -Value ('"' + $exe + '" --menu')

# ---------------------------------------------------------------- 缓存
Write-Step (T 'stepCache')
$ie4uinit = Join-Path $env:WINDIR 'System32\ie4uinit.exe'
if (Test-Path -LiteralPath $ie4uinit) {
    & $ie4uinit -show 2>$null | Out-Null
    & $ie4uinit -ClearIconCache 2>$null | Out-Null
}

# ---------------------------------------------------------------- 结果
Write-Step (T 'stepDone')
if ($rewrite) {
    $verify = (New-Object -ComObject WScript.Shell).CreateShortcut($link.FullName)
    Write-Host ('    ' + (T 'target') + ': ' + $verify.TargetPath)
    Write-Host ('    ' + (T 'args')   + ': ' + $verify.Arguments)
    Write-Host ('    ' + (T 'icon')   + ': ' + $verify.IconLocation)
    [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($verify)
}
Write-Host ''
Write-Host (T 'usage1') -ForegroundColor Gray
Write-Host (T 'usage2') -ForegroundColor Gray
Write-Host (T 'usage3') -ForegroundColor Gray
