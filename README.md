# 电源按钮（PowerButton）

把任务栏上那个「一点就立刻关机」的 `shutdown` 快捷方式，换成一个**可撤销的延迟关机按钮**。

- 没有日志、没有网络、没有后台服务、不需要管理员权限。
- 写操作只有三处：`%LOCALAPPDATA%\PowerButton\` 下的程序与图标、任务栏快捷方式本身、一条 `HKCU` 右键菜单注册项。

## 交互

| 操作 | 结果 |
| --- | --- |
| **左键单击** | 弹出 5 秒倒计时窗，倒数结束才真正关机 |
| **倒计时期间再点一次** | 取消：直接关掉窗口，系统里本来就没有排入任何关机 |
| **点窗口里的「取消关机」** | 同上 |
| **Shift + 左键** | 打开电源菜单 |
| **右键快捷方式 → 电源菜单** | 同上（要在右键菜单里再选一次「电源菜单」，共两次点击） |
| **鼠标悬停** | 显示快捷方式自带的提示气泡（内容写在 `.lnk` 的备注字段里，由资源管理器绘制） |

电源菜单：睡眠 / 重启 / 关机 / 注销 / 锁定 / 关闭显示器 / 定时关机… / 取消关机 / 倒计时设置…

重启走的是同样的倒计时，一样能取消。

### 手势的能力边界（为什么只有这几种）

任务栏按钮是**资源管理器画的一层壳**，不是这个程序自己的窗口：

- 按钮的按下、长按、右键都发生在资源管理器进程里；本程序是在**点击之后**才被启动的，天生收不到这些事件。
- `.lnk` 只能表达「点一下 → 启动某个程序」。想做到「长按」或「右键直接弹菜单」，必须常驻一个后台进程 + 低级鼠标钩子（`WH_MOUSE_LL`）+ 自己算出按钮的屏幕矩形，代价是常驻内存、开机自启、每次改分辨率/DPI/多屏布局都要重算、资源管理器重启要重新挂钩。
- 这里选择**不常驻**：只保留 `.lnk` 能力范围内的做法——左键走程序、Shift+左键走菜单、右键借助注册的 shell 动词（多一次点击）。悬停提示同样交给 `.lnk` 的备注字段，程序本身不参与。

**不会出现 Windows 自带的「将在 1 分钟内关机」提示**：那个提示是关机*宽限期*的产物。本程序不预先把关机排进系统（不用 `shutdown /t`），倒计时完全由自己的窗口表现，数到 0 才用 `ExitWindowsEx` 发起关机——和开始菜单里的「关机」是同一条路径，没有宽限期，也就没有那个提示框。

## 为什么这么设计

- **不加确认框**：确认框每次都要多一次点击，日常使用反而更烦；用「倒计时 + 随时可撤销」把不可逆的瞬间动作变成可逆的延迟动作。
- **不预先排期、不用 `shutdown.exe /t`**：Windows 的「即将关机 / 1 分钟内关机」警告框属于宽限期行为，一用 `/t` 就会被拉出来。改成「自己数秒 → 到点直接调 `ExitWindowsEx`」，既没有两个倒计时打架，也没有系统弹窗。副作用是取消变得彻底干净：什么都没排进系统，关掉窗口即取消。
- **不带 `/f`**：保留 Windows 自己的「这些应用阻止关机」保护，避免强杀丢数据。
- **关机仍是混合关机的快速启动路径**：`EWX_SHUTDOWN` 与开始菜单「关机」一致，已注册恢复的应用（浏览器标签、编辑器）照旧能在下次登录时回来。
- **按钮位置不变**：原地改写同一个 `.lnk` 文件，任务栏固定位置、图标位置都不动。

## 安装

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1
```

脚本会：准备图标（复制 `icons\power.ico`，缺失时现场生成）→ 用系统自带的 `csc.exe` 编译 `src\PowerButton.cs` → 备份原快捷方式 → 原地改写 `shutdown.lnk` → 注册右键动词 → 刷新图标缓存。

可重复运行（升级时直接重跑）。原快捷方式只在第一次安装时备份一次。

如果任务栏图标没立刻变化，注销/重启一次资源管理器即可（快捷方式已经改好了，只是图标缓存）。

## 卸载 / 回滚

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\uninstall.ps1
```

会把 `shutdown.lnk` 还原成备份里的原版（指向 `shutdown.exe -s -t 0`），删掉 `HKCU` 注册项和 `%LOCALAPPDATA%\PowerButton` 目录。

## 自定义倒计时

右键 → 电源菜单 → **倒计时设置…**，可选 3 / 5 / 8 / 10 / 15 秒，写入 `%LOCALAPPDATA%\PowerButton\config.ini`：

```ini
delay=5
lang=auto
```

`delay=0` 表示立即执行（不建议）。

## 命令行参数（调试用）

```
PowerButton.exe                  # 左键行为：倒计时关机
PowerButton.exe --menu           # 直接打开电源菜单
PowerButton.exe --preview 30     # 只显示 30 秒倒计时窗口，不真的关机
PowerButton.exe --cancel         # 取消「定时关机」排下的关机（shutdown /a）
PowerButton.exe --delay 8        # 本次用 8 秒
PowerButton.exe --selftest       # 只检查能否取得关机权限，退出码 0=正常 2=失败
PowerButton.exe --set-description <lnk> <文本>   # 写快捷方式备注（安装脚本用）
PowerButton.exe --set-description <lnk> --file <utf8 文件>   # 同上，避开命令行空格拆分
PowerButton.exe --lnk-probe <lnk> <输出文件>     # 把 .lnk 里的备注原样读出来核对
PowerButton.exe --lang ja|zh|en     # 临时指定界面语言（默认跟随 Windows 显示语言）
PowerButton.exe --label-out <键> <输出文件>   # 按当前语言导出文案（install.ps1 用）
```

`--preview` 是唯一安全的可视化测试方式：它完整走倒计时窗口、取消按钮、再点一次取消的逻辑，只是不执行关机。

`--selftest` 用来验证 `SeShutdownPrivilege` 能不能拿到（`ExitWindowsEx` 的前置条件），不会关机。

## 文件

```
src\PowerButton.cs     主程序（C# 5，WinForms，无外部依赖）
src\app.manifest       DPI 感知清单（150% 缩放下文字不糊）
install.ps1            安装 / 升级
uninstall.ps1          卸载 / 回滚
icons\power.ico        多尺寸图标（16~256，9 个尺寸）
tools\make-icon.ps1    重新生成 power.ico
tools\shot.ps1         DPI 感知截图小工具（开发时验证 UI 用）
icons\raw\             候选图标的原始 SVG（15 款，留作备选）
```

安装后：

```
%LOCALAPPDATA%\PowerButton\PowerButton.exe
%LOCALAPPDATA%\PowerButton\power.ico
%LOCALAPPDATA%\PowerButton\config.ini           # 首次改设置后生成
%LOCALAPPDATA%\PowerButton\backup\shutdown.lnk.bak
```

## 图标

白圆盘 + 深色电源字形（`#1a1a1a`），字形取自 **Material Symbols Rounded** 的 `power_settings_new`（Apache-2.0）。

`icons\power.ico` 里是 16/20/24/32/40/48/64/128/256 九个尺寸**各自原生渲染**的位图，不是一张大图缩出来的——任务栏用 24~40px，缩小采样会让细笔画糊掉。

需要换形状或换颜色时：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\make-icon.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1
```

脚本用 Chrome headless 渲染 SVG（自动标定字形占图标边长的比例），再把位图打成 ICO。ICO 的每个条目都是手写的 32bpp DIB，因为：

- PNG 压缩条目 GDI+/Shell 不认，任务栏会显示空白占位图；
- `GetHicon()` → `Icon.Save()` 往返会丢掉 alpha，任务栏显示成黑方块。

改完图标若没立刻生效：停掉 `explorer`，删 `%LOCALAPPDATA%\Microsoft\Windows\Explorer\iconcache*.db`，再启动 `explorer`。

## 实现要点

- 单实例互斥体 `Local\PowerButton.Countdown`：第一次点击启动倒计时进程，第二次点击发现互斥体已被占用，就通过命名事件 `Local\PowerButton.Countdown.Cancel` 通知第一个进程取消。
- 倒计时期间系统侧什么都没发生，所以取消 = 关闭窗口；真正关机发生在倒计时归零之后，由 `ExitWindowsEx(EWX_SHUTDOWN)` 发起（`SeShutdownPrivilege` 在调用前临时启用）。这条 API 失败时退回到 `shutdown /sg /t 0`。
- 定时关机是例外：它需要关机进程在后代进程退出后仍存活，所以仍然用 `shutdown /sg /t N`，到剩余 1 分钟时会出现 Windows 的警告框——这是排期关机的固有行为。
- 用 `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe` 编译，机器上不需要装 .NET SDK；`/codepage:65001` 保证源码里的中文不乱码。
- **快捷方式备注必须绕开 `WScript.Shell`**：它按系统 ANSI 代码页写 `.lnk`，在日文（CP932）系统上「键、计、时、关、电、单」这些简体字会变成 `?`。程序里改用 `IShellLinkW` + `IPersistFile`（全程 UTF-16）：先 `Load` 原文件、再 `SetDescription`、再 `Save`——不 `Load` 直接保存会写出一个**空链接**，把目标、参数、图标一起抹掉。写完还会读回来逐字核对，不一致就报错，避免静默失败。
- 给 GUI 程序传参时注意：`Start-Process -ArgumentList` 不会替含空格的路径加引号（`...\Quick Launch\...` 就有空格），要自己拼 `"`。

## 许可

本仓库代码随你处置。内置图标来自 Google Material Symbols（Apache-2.0）；`icons\raw\` 下的候选依次来自 Material Symbols、Fluent UI Icons、Lucide（ISC）、Tabler、Phosphor、Bootstrap Icons、Heroicons、Carbon、Ionicons（均 MIT/Apache-2.0）与 Font Awesome Free（图标为 CC BY 4.0，如改用需署名）。


## 多语言 / Languages

界面有**中文、日本語、English** 三套文案，默认跟随 Windows 显示语言（`CultureInfo.CurrentUICulture`）：
`ja` → 日本語，`zh` → 中文，其他 → English。本机是 `ja-JP`，所以装完默认就是日文界面。

想换语言，三种办法（优先级从高到低）：

```powershell
PowerButton.exe --lang zh                     # 临时：只对这一条命令生效
$env:POWERBUTTON_LANG = 'zh'                  # 临时：对当前会话里启动的程序生效
# %LOCALAPPDATA%\PowerButton\config.ini 里写 lang=zh（或 ja / en / auto）
```

细节：

- 字体按语言切换：中文 `Microsoft YaHei UI`、日文 `Yu Gothic UI`、英文 `Segoe UI`——混用会出现「借字形」（日文汉字用中文字形渲染）的问题。
- 菜单宽度按最长的一条量出来，日文 `時間を指定してシャットダウン…` 不会被截断。
- 快捷方式悬停说明（`.lnk` 备注）和右键动词名也由程序按同一语言生成，装完即是系统语言的写法；换语言后重跑一次 `install.ps1` 即可刷新这两处。