<div align="center">

<img src="docs/logo.png" width="88" alt="Taskbar Power Button">

# 任务栏电源按钮

**一个可以反悔的关机按钮。**

[![release](https://img.shields.io/badge/release-v1.0.0-c8352c)](https://github.com/doggybear233/taskbar-power-button/releases) [![platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078d4)](#环境要求) [![license](https://img.shields.io/badge/license-MIT-3d9970)](LICENSE) [![ui](https://img.shields.io/badge/UI-中文%20%C2%B7%20日本語%20%C2%B7%20English-555555)](#多语言)

[安装](#安装) · [用法](#用法) · [设置](#设置) · [工作原理](#工作原理) · [已知限制](#已知限制)

[English](README.md) · **中文** · [日本語](README.ja.md)

</div>

---

点一下电源按钮，屏幕角落落下一张深色小卡片：红色圆环在走，中间是剩余秒数，旁边一行字说明将要发生什么。再点一次——按钮上任意位置，或按 `Esc`——它就消失，像什么都没发生过。

这就是全部想法。**一键关机，几秒钟反悔。** 不要确认框，不要「再点一次才算数」，也不用每次绕回开始菜单。

卡片上还有第二个按钮，展开其余电源操作——睡眠、重启、注销、锁定、关屏、定时关机。菜单和按钮同一套画风：同样的圆角、同样的间距、同样的字体，任何缩放比例、任何系统语言下都一致。

<img src="docs/screenshots/01-taskbar.png" width="133" alt="电源按钮就排在「开始」旁边">

| 左键 | 取消 | 菜单 |
| :---: | :---: | :---: |
| <img src="docs/screenshots/02-countdown-zh.png" width="330"> | 再点一次按钮 · <kbd>Esc</kbd> · 点「取消关机」 | <img src="docs/screenshots/04-menu-zh.png" width="150"> |

<sub>同一窗口与菜单的英文、日文版本：</sub>

| English | 日本語 |
| :---: | :---: |
| <img src="docs/screenshots/02-countdown-en.png" width="300"><br><img src="docs/screenshots/04-menu-en.png" width="120"> | <img src="docs/screenshots/02-countdown-ja.png" width="300"><br><img src="docs/screenshots/04-menu-ja.png" width="120"> |

- **取消就在同一个按钮上**：不用去找那个窗口，按下的地方再按一下就撤销。
- **从不抢焦点**：卡片只是一个置顶小窗，后面的工作照常跑，键盘输入也还落在你正在用的程序里。
- **全部是画出来的**：圆环、菜单、间距、字体都由程序自己绘制，1080p 笔记本和 4K 显示器上都不会变形。

<sub>幕后很克制：没有日志、没有联网、没有常驻进程、不需要管理员权限。只在安装或改设置时写三个地方——任务栏快捷方式、`%LOCALAPPDATA%\PowerButton\`、一条 `HKCU` 外壳动词键。</sub>

## 安装

### 环境要求

Windows 10 / 11（x64），.NET Framework 4.8（系统自带）。不需要 .NET SDK，不需要 Node，不需要包管理器；安装**不需要**管理员权限。

<details open>
<summary><b>从 Release 安装（推荐）</b></summary>

1. 到 [Releases](https://github.com/doggybear233/taskbar-power-button/releases) 下载 `taskbar-power-button-1.0.0.zip`，解压到任意目录。
2. 右键 `install.ps1` → **使用 PowerShell 运行**。（或命令行：`powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1`）
3. 如果任务栏图标没立刻变，注销再登录一次——快捷方式其实已经改好了，只是资源管理器的图标缓存没刷新。

重跑 `install.ps1` 即原地升级。原快捷方式只在第一次安装时备份。
</details>

<details>
<summary><b>从源码安装</b></summary>

```powershell
git clone https://github.com/doggybear233/taskbar-power-button.git
cd taskbar-power-button
powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1
```

`install.ps1` 会准备图标 → 用 .NET Framework 自带的 `csc.exe` 编译 `src\PowerButton.cs` → 备份原快捷方式 → 原地改写 → 注册 `HKCU` 外壳动词 → 刷新外壳。
</details>

## 用法

| 操作 | 结果 |
| --- | --- |
| **左键单击** | 弹出倒计时窗（默认 5 秒）；数到 0 才真正关机 |
| **倒计时期间再点一次** | 取消。系统侧本来就没排入任何关机，所以没有东西需要中止 |
| **<kbd>Esc</kbd> 或卡片上的「取消关机」** | 同上 |
| **<kbd>Shift</kbd> + 左键** | 直接打开电源菜单（不启动倒计时） |
| **卡片上的「电源菜单」按钮** | 不等倒计时结束，打开同一个菜单 |
| **鼠标悬停** | 快捷方式自带的提示气泡，由资源管理器绘制 |

菜单包含 **睡眠 · 重启 · 关机 · 注销 · 锁定 · 关闭显示器 · 定时关机… · 取消关机 · 倒计时设置…**。重启也走同一条可取消的倒计时。

悬停说明、卡片和菜单里的文案都由程序按系统语言生成，所以在日文、中文、英文机器上读起来都是母语写法。

## 功能

| 功能 | 为什么要有 |
| --- | --- |
| 可取消的倒计时 | 把不可逆的瞬间动作变成可撤销的延迟动作；比确认框便宜——正常关机不需要多点一次 |
| 再点即取消 | 就地用同一个按钮撤销，不必去找那个窗口 |
| 原生电源菜单 | 睡眠、重启、注销、锁定、关屏都不用绕开始菜单 |
| 不弹 Windows 关机提示 | Windows 只在关机被*排期*时才弹「将在 1 分钟内关机」；本程序不预排，倒计时结束才调 `ExitWindowsEx`，所以不弹框 |
| 从不强杀程序 | 从不使用 `shutdown.exe /f` 或 `EWX_FORCE`：拒绝关闭的程序仍会走系统「这些应用阻止关机」那一步，未保存的工作不会被销毁 |
| 原位替换 | 原地改写已有的 `.lnk`，不新建第二个固定项 |
| 多语言 | 界面、悬停说明、卡片与菜单都跟随 Windows 显示语言 |
| 单文件、无运行时 | 约 400 KB 的 WinForms 程序，由 `csc.exe` 编译；无需安装、不常驻 |

## 设置

菜单里的 **倒计时设置…** 会写入 `%LOCALAPPDATA%\PowerButton\config.ini`：

```ini
delay=5
lang=auto
```

`delay` 取 0–120 秒（`0` 表示立即执行，不建议）。`lang` 取 `auto` / `zh` / `ja` / `en`。

## 命令行

```
PowerButton.exe                     左键行为：倒计时后关机
PowerButton.exe --menu              直接打开电源菜单
PowerButton.exe --preview 30        只显示 30 秒倒计时，不关机
PowerButton.exe --cancel            取消菜单里「定时关机」排下的关机（shutdown /a）
PowerButton.exe --delay 8           本次用 8 秒
PowerButton.exe --lang ja|zh|en     本次强制界面语言
PowerButton.exe --selftest          检查能否取得 SeShutdownPrivilege（0=正常，2=失败）
PowerButton.exe --set-description <lnk> <文本>   写快捷方式备注（安装脚本用）
PowerButton.exe --lnk-probe <lnk> <输出>         把备注读出来核对
PowerButton.exe --label-out <键> <文件>          按当前语言导出一条文案
```

`--preview` 是安全的看界面方式：完整走倒计时窗口与取消逻辑，只是最后不关机。`--selftest` 只验证 `ExitWindowsEx` 需要的权限，同样不关机。

## 工作原理

<img src="docs/diagrams/flow.png" alt="状态图：点击 → 倒计时 → 取消或到期">

<img src="docs/diagrams/architecture.png" alt="结构图：快捷方式、可执行文件、Win32 调用">

点击启动进程；进程自己数秒；只有到 0 时才提升 `SeShutdownPrivilege` 并调用 `ExitWindowsEx(EWX_SHUTDOWN)`。取消就是关掉窗口——因为从未向 Windows 注册过任何东西，取消意味着**什么都没发生**。

## 编译

```powershell
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe /optimize+ `
  /codepage:65001 /win32manifest:src\app.manifest /win32icon:icons\power.ico `
  /out:%LOCALAPPDATA%\PowerButton\PowerButton.exe `
  /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll `
  src\PowerButton.cs
```

不需要 SDK、不需要工程文件：`csc.exe` 是 .NET Framework 的一部分，任何 Win10/11 都有。`/codepage:65001` 是因为源码是 UTF-8。CI 跑的是同一条命令，见 [`.github/workflows/build.yml`](.github/workflows/build.yml)。

重新生成图标：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\make-icon.ps1
```

`icons\power.ico` 内含 9 个尺寸（16 / 20 / 24 / 32 / 40 / 48 / 64 / 128 / 256），每个都按自身尺寸原生渲染而不是从一张大图缩出来——任务栏用 24–40 px，缩小采样会把细笔画糊掉。每个 ICO 条目都是手写的 32bpp DIB：GDI+ 和外壳不认 PNG 压缩条目（显示空白 tile），而 `GetHicon()` → `Icon.Save()` 往返会静默丢掉 alpha（变成黑方块）。

## 多语言

界面有 **中文 / 日本語 / English** 三套文案，默认跟随 Windows 显示语言（`CultureInfo.CurrentUICulture`）：`ja` → 日文，`zh` → 中文，其他 → 英文。

覆盖方式，优先级从低到高：

```powershell
# %LOCALAPPDATA%\PowerButton\config.ini → lang=zh
$env:POWERBUTTON_LANG = 'zh'
PowerButton.exe --lang zh
```

字体随语言切换（`Microsoft YaHei UI`、`Yu Gothic UI`、`Segoe UI`），避免日文汉字被中文字形渲染；菜单宽度按最长的一条量出来，日文长标签不会被截断。换语言后想刷新悬停说明，重跑一次 `install.ps1`。

## 已知限制

- **右键任务栏按钮弹出的是资源管理器的菜单，不是我们的；长按也没有反应。** 任务栏按钮是资源管理器的；固定快捷方式只能「启动一个程序」，而程序是在点击*之后*才启动的，收不到按下、长按、右键事件——注册的动词也不会出现在固定项上。真要做需要常驻进程 + `WH_MOUSE_LL` 低级鼠标钩子 + 每次分辨率/DPI/多屏变化重算按钮矩形。本项目刻意不常驻，所以菜单的两个入口是 <kbd>Shift</kbd> + 左键，以及卡片上的「电源菜单」按钮。
- **菜单里的「定时关机…」是唯一仍走 `shutdown.exe` 的路径**，因为排期的关机必须在本进程退出后继续存活；到剩余 60 秒时 Windows 会弹它自己的提示框——这是排期关机的固有行为。
- **不处理运行中切换 DPI 的显示器。** 界面按启动时的 DPI 缩放；把窗口拖到不同缩放比的显示器上，可能需要重开才恢复合适大小。
- 在 Windows 11 24H2/25H2、150% 缩放下测试。Windows 10 应当可用（用到的 API 都很老），但未实测。

## 卸载

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\uninstall.ps1
```

把 `shutdown.lnk` 还原成安装时备份的原版（`shutdown.exe -s -t 0`），删掉 `HKCU` 里的动词和 `%LOCALAPPDATA%\PowerButton`。除此之外没有改动任何地方。

## 致谢与许可

- 代码：[MIT](LICENSE)。
- 图标：电源字形取自 **Material Symbols** 的 `power_settings_new`（Google LLC，Apache-2.0），改成白圆底徽标并栅格化成 9 个尺寸。见 [NOTICE](NOTICE) 与 [licenses/Apache-2.0.txt](licenses/Apache-2.0.txt)。
- 选型阶段比较过 Material Symbols、Fluent UI System Icons、Lucide（ISC）、Tabler、Phosphor、Bootstrap Icons、Heroicons、Carbon、Ionicons（MIT/Apache-2.0）；最终只分发 Material Symbols 那一款。
- 与 Microsoft 无关。`shutdown.exe`、`ExitWindowsEx` 和 Windows 属于 Microsoft，本项目只是调用它们。
