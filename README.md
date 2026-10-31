# 电源按钮（PowerButton）

把任务栏上那个「一点就立刻关机」的 `shutdown` 快捷方式，换成一个**可撤销的延迟关机按钮**。

- 没有日志、没有网络、没有后台服务、不需要管理员权限。
- 写操作只有三处：`%LOCALAPPDATA%\PowerButton\` 下的程序与图标、任务栏快捷方式本身、一条 `HKCU` 右键菜单注册项。

## 交互

| 操作 | 结果 |
| --- | --- |
| **左键单击** | 5 秒倒计时关机（`shutdown /sg /t 5`），右下角弹出倒计时窗 |
| **倒计时期间再点一次** | 立即取消（等价于 `shutdown /a`） |
| **点窗口里的「取消关机」** | 同上 |
| **Shift + 左键** | 打开电源菜单 |
| **右键快捷方式 → 电源菜单** | 同上（需在快捷方式右键菜单里选「电源菜单」） |

电源菜单：睡眠 / 重启 / 关机 / 注销 / 锁定 / 关闭显示器 / 定时关机… / 取消关机 / 倒计时设置…

重启走的是同样的倒计时（`shutdown /r /t N`），一样能取消。

## 为什么这么设计

- **不加确认框**：确认框每次都要多一次点击，日常使用反而更烦；用「倒计时 + 随时可撤销」把不可逆的瞬间动作变成可逆的延迟动作。
- **不带 `/f`**：保留 Windows 自己的「这些应用阻止关机」保护，避免强杀丢数据。
- **用 `/sg` 而不是 `/s`**：关机并在下次登录时恢复已注册的应用（浏览器标签、编辑器等），会话能续上。
- **按钮位置不变**：原地改写同一个 `.lnk` 文件，任务栏固定位置、图标位置都不动。

## 安装

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1
```

脚本会：生成图标 → 用系统自带的 `csc.exe` 编译 `src\PowerButton.cs` → 备份原快捷方式 → 原地改写 `shutdown.lnk` → 注册右键动词 → 刷新图标缓存。

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
PowerButton.exe --cancel         # 直接执行 shutdown /a
PowerButton.exe --delay 8        # 本次用 8 秒
```

`--preview` 是唯一安全的可视化测试方式：它完整走倒计时窗口、取消按钮、再点一次取消的逻辑，只是不调用 `shutdown.exe`。

## 文件

```
src\PowerButton.cs     主程序（C# 5，WinForms，无外部依赖）
src\app.manifest       DPI 感知清单（150% 缩放下文字不糊）
install.ps1            安装 / 升级
uninstall.ps1          卸载 / 回滚
tools\shot.ps1         DPI 感知截图小工具（开发时验证 UI 用）
```

安装后：

```
%LOCALAPPDATA%\PowerButton\PowerButton.exe
%LOCALAPPDATA%\PowerButton\power.ico
%LOCALAPPDATA%\PowerButton\config.ini           # 首次改设置后生成
%LOCALAPPDATA%\PowerButton\backup\shutdown.lnk.bak
```

## 实现要点

- 单实例互斥体 `Local\PowerButton.Countdown`：第一次点击启动倒计时进程，第二次点击发现互斥体已被占用，就通过命名事件 `Local\PowerButton.Countdown.Cancel` 通知第一个进程取消。
- 窗口被意外关闭（Alt+F4、会话结束）时也会兜底 `shutdown /a`，不留下无法取消的关机。
- 用 `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe` 编译，机器上不需要装 .NET SDK；`/codepage:65001` 保证源码里的中文不乱码。
