#requires -Version 5.1
<#
    uninstall.ps1 - 还原任务栏快捷方式并删除本程序。
    uninstall.ps1 - restores the taskbar shortcut and removes the program.
    uninstall.ps1 - タスクバーのショートカットを元に戻し、プログラムを削除します。
#>

param(
    # 连同备份一起删除（默认保留备份，方便以后再装）。
    [switch] $Purge,
    # 明确指定要还原的快捷方式路径（默认读安装时记下的路径）。
    [string] $LnkPath
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

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
        stop        = '结束运行中的实例'
        verb        = '删除外壳动词'
        restore     = '还原快捷方式'
        purge       = '删除程序目录'
        done        = '完成'
        noBackup    = '没有找到备份，无法自动还原快捷方式。'
        noBackupTip = '你可以手动把任务栏上的按钮删掉，再重新固定 shutdown.exe。'
        restored    = '已还原到 {0}'
        linkKept    = '备份里的原始路径已不存在，跳过还原：{0}'
        removedVerb = '已删除右键动词注册项'
        removedDir  = '已删除 {0}'
        keptDir     = '程序文件已删除，备份保留在：{0}'
        fallback    = '没有备份，但当前按钮指向本程序；已把目标改回 shutdown.exe。'
    }
    ja = @{
        stop        = '実行中のインスタンスを終了'
        verb        = 'シェル動詞を削除'
        restore     = 'ショートカットを元に戻す'
        purge       = 'プログラムのフォルダーを削除'
        done        = '完了'
        noBackup    = 'バックアップが見つからないため、ショートカットを自動で戻せません。'
        noBackupTip = 'タスクバーのボタンを手動で外し、shutdown.exe を固定し直してください。'
        restored    = '{0} に戻しました'
        linkKept    = 'バックアップにある元のパスが存在しないため、復元をスキップしました: {0}'
        removedVerb = 'シェル動詞の登録を削除しました'
        removedDir  = '{0} を削除しました'
        keptDir     = 'プログラム本体は削除しました。バックアップは残しています: {0}'
        fallback    = 'バックアップはありませんが、現在のボタンは本プログラムを指していたため、リンク先を shutdown.exe に戻しました。'
    }
    en = @{
        stop        = 'Stopping the running instance'
        verb        = 'Removing the shell verb'
        restore     = 'Restoring the shortcut'
        purge       = 'Deleting the program folder'
        done        = 'Done'
        noBackup    = 'No backup was found, so the shortcut cannot be restored automatically.'
        noBackupTip = 'Remove the button from the taskbar and pin shutdown.exe again.'
        restored    = 'Restored to {0}'
        linkKept    = 'The original path from the backup no longer exists; skipped: {0}'
        removedVerb = 'Removed the shell verb registration'
        removedDir  = 'Deleted {0}'
        keptDir     = 'Removed the program files; the backup is kept in {0}'
        fallback    = 'No backup exists, but the current button pointed at this program; its target is back to shutdown.exe.'
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

$outDir  = Join-Path $env:LOCALAPPDATA 'PowerButton'
$backup  = Join-Path $outDir 'backup'
$bakFile = Join-Path $backup 'shutdown.lnk.bak'
$linkRec = Join-Path $backup 'link.txt'
$defLink = Join-Path $env:APPDATA 'Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar\shutdown.lnk'

Write-Step (T 'stop')
Get-Process -Name 'PowerButton' -ErrorAction SilentlyContinue | ForEach-Object {
    try { $_.Kill(); $_.WaitForExit(3000) } catch { }
}

Write-Step (T 'verb')
$verb = 'HKCU:\Software\Classes\Applications\PowerButton.exe'
if (Test-Path -LiteralPath $verb) {
    Remove-Item -LiteralPath $verb -Recurse -Force
    Write-Host ('    ' + (T 'removedVerb'))
}

Write-Step (T 'restore')
$target = $LnkPath
if (-not $target -and (Test-Path -LiteralPath $linkRec)) {
    $target = (Get-Content -LiteralPath $linkRec -Raw -Encoding UTF8).Trim()
}
if (-not $target) { $target = $defLink }

if (Test-Path -LiteralPath $bakFile) {
    if (Test-Path -LiteralPath (Split-Path -Parent $target)) {
        Copy-Item -LiteralPath $bakFile -Destination $target -Force
        Write-Host ('    ' + (Tf 'restored' @($target)))
    }
    else {
        Write-Host ('    ' + (Tf 'linkKept' @($target))) -ForegroundColor Yellow
    }
}
else {
    # 没有备份：如果这个按钮是我们的，就把目标改回去，至少不留一个指向即将删除的程序的快捷方式。
    if (Test-Path -LiteralPath $target) {
        $shell = New-Object -ComObject WScript.Shell
        $sc = $shell.CreateShortcut($target)
        if ($sc.TargetPath -ieq (Join-Path $outDir 'PowerButton.exe')) {
            $sc.TargetPath = (Join-Path $env:WINDIR 'System32\shutdown.exe')
            $sc.Arguments = '-s -t 0'
            $sc.WorkingDirectory = (Join-Path $env:WINDIR 'System32')
            $sc.IconLocation = (Join-Path $env:WINDIR 'System32\shutdown.exe') + ',0'
            $sc.Description = ''
            $sc.Save()
            Write-Host ('    ' + (T 'fallback')) -ForegroundColor Yellow
        }
        [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($sc)
        [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($shell)
    }
    else {
        Write-Host ('    ' + (T 'noBackup')) -ForegroundColor Yellow
        Write-Host ('    ' + (T 'noBackupTip')) -ForegroundColor Gray
    }
}

if ($Purge) {
    Write-Step (T 'purge')
    if (Test-Path -LiteralPath $outDir) {
        Remove-Item -LiteralPath $outDir -Recurse -Force
        Write-Host ('    ' + (Tf 'removedDir' @($outDir)))
    }
}
else {
    # 默认只删程序文件，保留 backup\ 以便以后还能还原/重装。
    foreach ($name in @('PowerButton.exe', 'power.ico', 'config.ini')) {
        $path = Join-Path $outDir $name
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue }
    }
    Write-Host ('    ' + (Tf 'keptDir' @($backup))) -ForegroundColor Gray
}

$ie4uinit = Join-Path $env:WINDIR 'System32\ie4uinit.exe'
if (Test-Path -LiteralPath $ie4uinit) {
    & $ie4uinit -show 2>$null | Out-Null
    & $ie4uinit -ClearIconCache 2>$null | Out-Null
}

Write-Step (T 'done')
