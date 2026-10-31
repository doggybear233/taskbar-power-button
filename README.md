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
| **右键快捷方式 → 电源菜单** | 同上（需在快捷方式右键菜单里选「电源菜单」） |

电源菜单：睡眠 / 重启 / 关机 / 注销 / 锁定 / 关闭显示器 / 定时关机… / 取消关机 / 倒计时设置…

重启走的是同样的倒计时，一样能取消。

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

## 许可

本仓库代码随你处置。内置图标来自 Google Material Symbols（Apache-2.0）；`icons\raw\` 下的候选依次来自 Material Symbols、Fluent UI Icons、Lucide（ISC）、Tabler、Phosphor、Bootstrap Icons、Heroicons、Carbon、Ionicons（均 MIT/Apache-2.0）与 Font Awesome Free（图标为 CC BY 4.0，如改用需署名）。
