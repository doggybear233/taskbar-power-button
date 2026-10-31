// PowerButton - a safer replacement for the taskbar "shutdown" shortcut.
// Left click  : countdown window, cancellable; shuts down only when it reaches 0
// Click again : cancels the pending countdown
// Shift+click : power menu, also available as a right-click verb
// No logging, no telemetry, no admin rights required.
//
// Compiled with the .NET Framework 4.x csc.exe (C# 5):
//   csc /target:winexe /codepage:65001 /win32icon:power.ico /out:PowerButton.exe PowerButton.cs

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace PowerButton
{
    internal enum PowerAction
    {
        Shutdown,
        Restart
    }

    // 任务栏几何。Windows 的「开始」按钮在哪，弹窗就落在哪旁边——任务栏
    // 横放在上/下、竖放在左/右，图标居中还是靠左，都跟着走。
    internal static class Taskbar
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Rect { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string windowName);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

        private static Rectangle ToRectangle(Rect r)
        {
            return new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
        }

        // 任务栏矩形与「开始」按钮矩形；拿不到就返回 false。
        internal static bool TryGet(out Rectangle bar, out Rectangle start)
        {
            bar = Rectangle.Empty;
            start = Rectangle.Empty;

            IntPtr tray = FindWindow("Shell_TrayWnd", null);
            if (tray == IntPtr.Zero) return false;

            Rect r;
            if (!GetWindowRect(tray, out r)) return false;
            bar = ToRectangle(r);
            if (bar.Width <= 0 || bar.Height <= 0) return false;

            IntPtr button = FindWindowEx(tray, IntPtr.Zero, "Start", null);
            if (button != IntPtr.Zero && GetWindowRect(button, out r))
            {
                Rectangle found = ToRectangle(r);
                if (found.Width > 0 && found.Height > 0) start = found;
            }

            if (start.IsEmpty)
            {
                // 兜底：Windows 版本换了类名也还能落个合理位置。
                if (bar.Width >= bar.Height) start = new Rectangle(bar.Left, bar.Top, bar.Height, bar.Height);
                else start = new Rectangle(bar.Left, bar.Top, bar.Width, bar.Width);
            }

            return true;
        }

        // 把一块 size 大小的窗口摆到「开始」按钮旁边、贴住任务栏，再夹进工作区。
        internal static Point SlotFor(Size size)
        {
            Rectangle bar, start;
            if (!TryGet(out bar, out start))
            {
                Rectangle fallback = Screen.PrimaryScreen.WorkingArea;
                return new Point(fallback.Right - size.Width - Ui.P(20), fallback.Bottom - size.Height - Ui.P(12));
            }

            Rectangle work = Screen.FromRectangle(bar).WorkingArea;
            int gap = Ui.P(10);
            int x, y;

            if (bar.Width >= bar.Height)
            {
                bool atBottom = bar.Top + bar.Height / 2 > work.Top + work.Height / 2;
                y = atBottom ? bar.Top - size.Height - gap : bar.Bottom + gap;
                x = start.Left + start.Width / 2 - size.Width / 2;
            }
            else
            {
                bool atRight = bar.Left + bar.Width / 2 > work.Left + work.Width / 2;
                x = atRight ? bar.Left - size.Width - gap : bar.Right + gap;
                y = start.Top + start.Height / 2 - size.Height / 2;
            }

            x = Math.Max(work.Left + gap, Math.Min(x, work.Right - size.Width - gap));
            y = Math.Max(work.Top + gap, Math.Min(y, work.Bottom - size.Height - gap));
            return new Point(x, y);
        }
    }

    internal static class Native
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern bool LockWorkStation();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        internal static readonly IntPtr HwndBroadcast = new IntPtr(0xffff);
        internal const int WmSysCommand = 0x0112;
        internal const int ScMonitorPower = 0xF170;

        // Windows 自带一个「将在 1 分钟内关机」的警告窗口，它是关机**宽限期**的提示。
        // 直接调 ExitWindowsEx（开始菜单「关机」走的就是这条）就没有宽限期，
        // 也就没有那个窗口——倒计时完全由本程序自己的窗口来表现。
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ExitWindowsEx(uint uFlags, uint dwReason);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr process, uint desiredAccess, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LookupPrivilegeValue(string system, string name, out long luid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll,
            ref TokenPrivileges newState, int bufferLength, IntPtr previousState, IntPtr returnLength);

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct TokenPrivileges
        {
            public int Count;
            public long Luid;
            public int Attributes;
        }

        private const uint TokenAdjustPrivileges = 0x0020;
        private const uint TokenQuery = 0x0008;
        private const int SePrivilegeEnabled = 0x0002;
        private const uint EwxShutdown = 0x0001;
        private const uint EwxReboot = 0x0002;

        private static bool EnableShutdownPrivilege()
        {
            IntPtr token;
            if (!OpenProcessToken(Process.GetCurrentProcess().Handle,
                                  TokenAdjustPrivileges | TokenQuery, out token))
                return false;

            try
            {
                long luid;
                if (!LookupPrivilegeValue(null, "SeShutdownPrivilege", out luid))
                    return false;

                TokenPrivileges state = new TokenPrivileges();
                state.Count = 1;
                state.Luid = luid;
                state.Attributes = SePrivilegeEnabled;

                if (!AdjustTokenPrivileges(token, false, ref state, 0, IntPtr.Zero, IntPtr.Zero))
                    return false;

                // AdjustTokenPrivileges 即使部分失败也返回 true，必须看 LastWin32Error。
                return Marshal.GetLastWin32Error() == 0;
            }
            finally
            {
                CloseHandle(token);
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        // 自检：只验证能否拿到 SeShutdownPrivilege，绝不真的关机。
        internal static bool CanEnableShutdownPrivilege()
        {
            return EnableShutdownPrivilege();
        }

        // 真正执行关机 / 重启。没有宽限期，因此不会出现 Windows 的关机警告窗口。
        internal static bool ExitShutdown(bool restart)
        {
            if (!EnableShutdownPrivilege())
                return false;

            return ExitWindowsEx(restart ? EwxReboot : EwxShutdown, 0);
        }

        internal static void RunShutdown(string arguments)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("shutdown.exe", arguments);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageDlg.Error(L.T("err.shutdownExe") + ex.Message);
            }
        }

        internal static bool AbortShutdown()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("shutdown.exe", "/a");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    if (p == null) return false;
                    if (!p.WaitForExit(4000))
                    {
                        try { p.Kill(); } catch { }
                        return false;
                    }
                    return p.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        internal static void MonitorOff()
        {
            SendMessage(HwndBroadcast, WmSysCommand, new IntPtr(ScMonitorPower), new IntPtr(2));
        }
    }

    // 写 .lnk 的「备注」（Explorer 悬停时显示的那行字）。
    // 不能用 WScript.Shell：它在非 Unicode 代码页的系统上按 ANSI 转码，中文会变成 '?'。
    // 这里直接走 IShellLinkW，全程 UTF-16。
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    internal class ShellLinkClass
    {
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
     Guid("000214F9-0000-0000-C000-000000000046")]
    internal interface IShellLinkW
    {
        void GetPath(StringBuilder file, int cch, IntPtr findData, uint flags);
        void GetIDList(out IntPtr pidl);
        void SetIDList(IntPtr pidl);
        void GetDescription(StringBuilder name, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory(StringBuilder dir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments(StringBuilder args, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation(StringBuilder iconPath, int cch, out int iconIndex);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pathRel, uint reserved);
        void Resolve(IntPtr hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
     Guid("0000010b-0000-0000-C000-000000000046")]
    internal interface IPersistFile
    {
        void GetClassID(out Guid classId);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string fileName,
                  [MarshalAs(UnmanagedType.Bool)] bool remember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string fileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string fileName);
    }

    internal static class Shortcut
    {
        [DllImport("ole32.dll")]
        private static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint ctx,
                                                   ref Guid iid, out IntPtr ppv);

        [DllImport("ole32.dll")]
        private static extern int OleInitialize(IntPtr reserved);

        private static readonly Guid ShellLinkClsid = new Guid("00021401-0000-0000-C000-000000000046");
        private static readonly Guid ShellLinkW_IID = new Guid("000214F9-0000-0000-C000-000000000046");

        private static object CreateShellLink()
        {
            Guid clsid = ShellLinkClsid;
            Guid iid = ShellLinkW_IID;
            IntPtr ptr;
            int hr = CoCreateInstance(ref clsid, IntPtr.Zero, 1 /*CLSCTX_INPROC_SERVER*/, ref iid, out ptr);
            if (hr != 0 || ptr == IntPtr.Zero)
                throw new COMException("CoCreateInstance failed", hr);

            try
            {
                return Marshal.GetObjectForIUnknown(ptr);
            }
            finally
            {
                Marshal.Release(ptr);
            }
        }

        internal static bool SetDescription(string lnkPath, string text)
        {
            object com = null;
            try
            {
                OleInitialize(IntPtr.Zero);
                com = CreateShellLink();
                IPersistFile file = (IPersistFile)com;
                // 必须先 Load：否则是个空链，Save 会把目标/参数/图标全抹掉。
                file.Load(lnkPath, 2); // STGM_READWRITE
                ((IShellLinkW)com).SetDescription(text);
                file.Save(lnkPath, true);
                // 写完再读回来核对：被别的进程占着文件时 Save 可能静默不生效。
                return ReadDescription(lnkPath) == text;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (com != null)
                {
                    try { Marshal.FinalReleaseComObject(com); }
                    catch { }
                }
            }
        }

        // 诊断用：把 .lnk 里那条备注按 UTF-16 原样读出来。
        internal static string ReadDescription(string lnkPath)
        {
            object com = null;
            try
            {
                OleInitialize(IntPtr.Zero);
                com = CreateShellLink();
                ((IPersistFile)com).Load(lnkPath, 0); // STGM_READ
                StringBuilder sb = new StringBuilder(1024);
                ((IShellLinkW)com).GetDescription(sb, sb.Capacity);
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return "<error " + ex.GetType().Name + ": " + ex.Message + ">";
            }
            finally
            {
                if (com != null)
                {
                    try { Marshal.FinalReleaseComObject(com); }
                    catch { }
                }
            }
        }
    }

    // 界面文案：中文 / 日本語 / English。
    // 语言按系统显示语言自动选择；可用 config.ini 的 lang= 或环境变量 POWERBUTTON_LANG 覆盖。
    internal static class L
    {
        internal enum Lang { Zh, Ja, En }

        private static Lang _lang = Lang.Zh;
        private static bool _ready;

        internal static Lang Current { get { return _lang; } }

        internal static bool IsCjk
        {
            get { return _lang == Lang.Zh || _lang == Lang.Ja; }
        }

        // 字体要和语言匹配：雅黑排日文汉字、Yu Gothic 排中文都会出现「借字形」，字形不对。
        internal static string FontFamily
        {
            get
            {
                if (_lang == Lang.Ja) return "Yu Gothic UI";
                if (_lang == Lang.En) return "Segoe UI";
                return "Microsoft YaHei UI";
            }
        }

        internal static void Init(string overrideCode)
        {
            if (_ready) return;
            _ready = true;

            string code = overrideCode;
            if (string.IsNullOrEmpty(code)) code = Config.Get("lang");
            if (string.IsNullOrEmpty(code)) code = Environment.GetEnvironmentVariable("POWERBUTTON_LANG");
            if (string.IsNullOrEmpty(code))
            {
                try { code = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName; }
                catch { code = "en"; }
            }

            code = code.Trim().ToLowerInvariant();
            if (code.Length == 0 || code == "auto" || code == "system")
            {
                try { code = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Trim().ToLowerInvariant(); }
                catch { code = "en"; }
            }

            if (code.StartsWith("zh")) _lang = Lang.Zh;
            else if (code.StartsWith("ja")) _lang = Lang.Ja;
            else _lang = Lang.En;
        }

        internal static string T(string key)
        {
            bool ja = _lang == Lang.Ja;
            bool en = _lang == Lang.En;

            switch (key)
            {
                case "app": return en ? "Power" : (ja ? "電源" : "电源");

                case "preview.title": return en ? "Preview — no action" : (ja ? "プレビュー（実行しません）" : "预览（不会执行）");
                case "shutdown.title": return en ? "Shutting down" : (ja ? "まもなくシャットダウン" : "即将关机");
                case "restart.title": return en ? "Restarting" : (ja ? "まもなく再起動" : "即将重启");
                case "preview.hint": return en ? "Interface check only — nothing will happen"
                    : (ja ? "画面の確認用です。シャットダウンはしません" : "仅测试界面，不会关机");
                case "cancel.hint": return en ? "Click the taskbar button again to cancel"
                    : (ja ? "タスクバーのボタンをもう一度押すと中止できます" : "再次点击任务栏按钮可取消");
                case "preview.button": return en ? "Close preview" : (ja ? "プレビューを閉じる" : "关闭预览");
                case "cancel.shutdown": return en ? "Cancel shutdown" : (ja ? "シャットダウンを中止" : "取消关机");
                case "cancel.restart": return en ? "Cancel restart" : (ja ? "再起動を中止" : "取消重启");
                case "card.menu": return en ? "Power menu" : (ja ? "電源メニュー" : "电源菜单");

                case "menu.sleep": return en ? "Sleep" : (ja ? "スリープ" : "睡眠");
                case "menu.restart": return en ? "Restart" : (ja ? "再起動" : "重启");
                case "menu.shutdown": return en ? "Shut down" : (ja ? "シャットダウン" : "关机");
                case "menu.signout": return en ? "Sign out" : (ja ? "サインアウト" : "注销");
                case "menu.lock": return en ? "Lock" : (ja ? "ロック" : "锁定");
                case "menu.displayoff": return en ? "Turn off display" : (ja ? "ディスプレイをオフ" : "关闭显示器");
                case "menu.timer": return en ? "Shut down later…" : (ja ? "時間を指定してシャットダウン…" : "定时关机…");
                case "menu.cancel": return en ? "Cancel shutdown" : (ja ? "シャットダウンを中止" : "取消关机");
                case "menu.delay": return en ? "Countdown settings…" : (ja ? "カウントダウン設定…" : "倒计时设置…");

                case "msg.nothingPending": return en ? "There is no shutdown or restart pending."
                    : (ja ? "実行待ちのシャットダウン／再起動はありません。" : "当前没有等待执行的关机或重启。");

                case "err.shutdownExe": return en ? "Could not run shutdown.exe: "
                    : (ja ? "shutdown.exe を実行できません：" : "无法调用 shutdown.exe：");
                case "err.generic": return en ? "Power button error: "
                    : (ja ? "電源ボタンでエラーが発生しました：" : "电源按钮出错：");

                case "config.header": return en ? "# Power button settings" : (ja ? "# 電源ボタンの設定" : "# 电源按钮设置");
                case "config.delay": return en ? "# delay = seconds to wait after a left click (0 = act immediately, not recommended)"
                    : (ja ? "# delay = 左クリック後に待つ秒数。0 は即時実行（非推奨）"
                          : "# delay = 左键单击后等待的秒数，0 表示立即执行（不建议）");
                case "config.lang": return en ? "# lang = zh / ja / en / auto (auto follows the Windows display language)"
                    : (ja ? "# lang = zh / ja / en / auto（auto は Windows の表示言語に従う）"
                          : "# lang = zh / ja / en / auto（auto 跟随 Windows 显示语言）");

                case "menu.verb": return en ? "Power menu(&M)" : (ja ? "電源メニュー(&M)" : "电源菜单(&M)");

                case "lnk.description": return en
                    ? "Left-click: shut down after a countdown (click again to cancel)  |  Shift+click or right-click menu: power menu"
                    : (ja
                        ? "左クリック：カウントダウン後にシャットダウン（もう一度押すと中止）　｜　Shift+クリックまたは右クリックメニュー：電源メニュー"
                        : "左键：倒计时关机，再点一次可取消　｜　Shift+左键 或 右键菜单：电源菜单");
            }

            return key;
        }

        internal static string TimerItem(int minutes)
        {
            if (_lang == Lang.Ja) return minutes + " 分後にシャットダウン";
            if (_lang == Lang.En) return "Shut down in " + minutes + " min";
            return minutes + " 分钟后关机";
        }

        internal static string DelayItem(int seconds, bool current)
        {
            string unit = _lang == Lang.En ? " s" : " 秒";
            string suffix = "";
            if (current) suffix = _lang == Lang.En ? "  (current)" : (_lang == Lang.Ja ? "（現在）" : "（当前）");
            return seconds + unit + suffix;
        }

        internal static string ScheduledInfo(int minutes)
        {
            if (_lang == Lang.Ja)
                return minutes + " 分後にシャットダウンするよう設定しました。\n\n中止する場合は、右クリックメニューの「シャットダウンを中止」から。";
            if (_lang == Lang.En)
                return "Shutdown scheduled in " + minutes + " minutes.\n\nTo cancel early, use \u201cCancel shutdown\u201d in the right-click menu.";
            return "已安排在 " + minutes + " 分钟后关机。\n\n要提前撤销，用右键菜单里的「取消关机」。";
        }
    }

    internal static class MessageDlg
    {
        internal static void Info(string text)
        {
            MessageBox.Show(text, L.T("app"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        internal static void Error(string text)
        {
            MessageBox.Show(text, L.T("app"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // Tiny key=value store next to the executable: %LOCALAPPDATA%\PowerButton\config.ini
    internal static class Config
    {
        private static readonly string Path =
            System.IO.Path.Combine(Program.BaseDir, "config.ini");

        private static Dictionary<string, string> _map;
        private static bool _loaded;

        // 全部键值都保留在内存里，写回时整份重写（config.ini 只有一两行，够用）。
        private static Dictionary<string, string> Map
        {
            get
            {
                if (_loaded) return _map;
                _loaded = true;
                _map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    if (File.Exists(Path))
                    {
                        string[] lines = File.ReadAllLines(Path);
                        for (int i = 0; i < lines.Length; i++)
                        {
                            string line = lines[i].Trim();
                            if (line.Length == 0 || line.StartsWith("#")) continue;
                            int eq = line.IndexOf('=');
                            if (eq <= 0) continue;
                            _map[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                        }
                    }
                }
                catch { }
                return _map;
            }
        }

        internal static string Get(string key)
        {
            string v;
            if (Map.TryGetValue(key, out v) && v.Length > 0) return v;
            return null;
        }

        internal static void Set(string key, string value)
        {
            Map[key] = value;
            Save();
        }

        internal static int Delay
        {
            get
            {
                int v;
                string raw = Get("delay");
                if (raw != null && int.TryParse(raw, out v) && v >= 0 && v <= 120) return v;
                return 5;
            }
            set
            {
                int v = value;
                if (v < 0) v = 0;
                if (v > 120) v = 120;
                Set("delay", v.ToString());
            }
        }

        private static void Save()
        {
            try
            {
                string lang = Get("lang");
                if (lang == null) lang = "auto";
                File.WriteAllLines(Path, new string[] {
                    L.T("config.header"),
                    L.T("config.delay"),
                    "delay=" + Delay.ToString(),
                    L.T("config.lang"),
                    "lang=" + lang
                });
            }
            catch { }
        }
    }

    internal sealed class RingView : Control
    {
        private int _left = 5;
        private double _fraction = 1.0;

        internal RingView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        internal void Update(int secondsLeft, double fraction)
        {
            _left = secondsLeft;
            _fraction = fraction < 0 ? 0 : (fraction > 1 ? 1 : fraction);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int pad = Ui.P(6);
            int size = Math.Min(Width, Height) - pad * 2;
            Rectangle box = new Rectangle(pad, pad, size, size);

            using (Pen track = new Pen(Color.FromArgb(64, 64, 64), Ui.F(5f)))
            {
                track.StartCap = LineCap.Round;
                track.EndCap = LineCap.Round;
                g.DrawEllipse(track, box);
            }

            if (_fraction > 0.001)
            {
                using (Pen arc = new Pen(Color.FromArgb(232, 17, 35), Ui.F(5f)))
                {
                    arc.StartCap = LineCap.Round;
                    arc.EndCap = LineCap.Round;
                    g.DrawArc(arc, box, -90f, (float)(360.0 * _fraction));
                }
            }

            string text = _left.ToString();
            float ratio = text.Length >= 3 ? 0.26f : 0.34f;

            using (Font f = new Font("Segoe UI", size * ratio, FontStyle.Bold, GraphicsUnit.Pixel))
            using (SolidBrush b = new SolidBrush(Color.White))
            {
                SizeF measure = g.MeasureString(text, f);
                g.DrawString(text, f, b,
                    box.X + (box.Width - measure.Width) / 2f,
                    box.Y + (box.Height - measure.Height) / 2f);
            }
        }
    }

    internal sealed class CountdownForm : Form
    {
        private readonly PowerAction _action;
        private readonly int _seconds;
        private readonly bool _preview;
        private readonly EventWaitHandle _cancelSignal;

        private readonly Stopwatch _watch = new Stopwatch();
        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer();
        private readonly RingView _ring = new RingView();
        private readonly Label _caption = new Label();
        private readonly Label _detail = new Label();
        private readonly Button _cancel = new Button();
        private readonly Button _menu = new Button();

        private bool _finished;
        private bool _cancelling;

        // 倒计时走完（而不是被取消）时为 true：由调用方在这之后真正执行关机。
        internal bool Expired;

        // 卡片上的「电源菜单」被点时置位：取消这次倒计时，改由调用方打开菜单。
        internal bool OpenMenuRequested;

        // 任务栏按钮被快速点了第二下（双击）：同样放弃倒计时，直接开菜单。
        internal bool DoubleClicked;

        internal CountdownForm(PowerAction action, int seconds, EventWaitHandle signal, bool preview)
        {
            _action = action;
            _seconds = seconds;
            _cancelSignal = signal;
            _preview = preview;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(Ui.P(380), Ui.P(156));
            BackColor = Color.FromArgb(32, 32, 32);
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            Text = L.T("app");

            _ring.SetBounds(Ui.P(18), Ui.P(20), Ui.P(84), Ui.P(84));

            _caption.SetBounds(Ui.P(116), Ui.P(22), Ui.P(248), Ui.P(30));
            _caption.Font = new Font(L.FontFamily, 13.5f, FontStyle.Bold);
            _caption.ForeColor = Color.White;
            _caption.BackColor = Color.Transparent;
            _caption.Text = preview ? L.T("preview.title")
                : (action == PowerAction.Restart ? L.T("restart.title") : L.T("shutdown.title"));

            _detail.SetBounds(Ui.P(116), Ui.P(56), Ui.P(248), Ui.P(40));
            _detail.Font = new Font(L.FontFamily, 9f);
            _detail.ForeColor = Color.FromArgb(168, 168, 168);
            _detail.BackColor = Color.Transparent;
            _detail.Text = preview
                ? L.T("preview.hint")
                : L.T("cancel.hint");

            _cancel.SetBounds(Ui.P(18), Ui.P(112), Ui.P(218), Ui.P(34));
            _cancel.FlatStyle = FlatStyle.Flat;
            _cancel.FlatAppearance.BorderSize = 0;
            _cancel.FlatAppearance.MouseOverBackColor = Color.FromArgb(198, 12, 28);
            _cancel.BackColor = Color.FromArgb(232, 17, 35);
            _cancel.ForeColor = Color.White;
            _cancel.Font = new Font(L.FontFamily, 9.5f, FontStyle.Bold);
            _cancel.Text = preview ? L.T("preview.button")
                : (action == PowerAction.Restart ? L.T("cancel.restart") : L.T("cancel.shutdown"));
            _cancel.Cursor = Cursors.Hand;
            _cancel.Click += OnCancelClick;

            // 右键在任务栏固定项上归 Explorer 管，快捷方式收不到；菜单挂在卡片上，
            // 一次点击关机、再一眼就能看到菜单入口。
            _menu.SetBounds(Ui.P(242), Ui.P(112), Ui.P(120), Ui.P(34));
            _menu.FlatStyle = FlatStyle.Flat;
            _menu.FlatAppearance.BorderSize = 0;
            _menu.FlatAppearance.MouseOverBackColor = Color.FromArgb(78, 78, 78);
            _menu.BackColor = Color.FromArgb(54, 54, 54);
            _menu.ForeColor = Color.FromArgb(224, 224, 224);
            _menu.Font = new Font(L.FontFamily, 9f);
            _menu.Text = L.T("card.menu");
            _menu.Cursor = Cursors.Hand;
            _menu.Visible = !preview;
            _menu.Click += OnMenuClick;

            Controls.Add(_ring);
            Controls.Add(_caption);
            Controls.Add(_detail);
            Controls.Add(_cancel);
            Controls.Add(_menu);

            _timer.Interval = 40;
            _timer.Tick += OnTick;

            Load += OnLoad;
            FormClosing += OnFormClosing;
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        private void OnLoad(object sender, EventArgs e)
        {
            using (GraphicsPath path = Drawing.RoundedPath(ClientRectangle, Ui.P(14)))
            {
                Region = new Region(path);
            }

            Location = Taskbar.SlotFor(Size);

            _ring.Update(_seconds, 1.0);
            _watch.Start();
            _timer.Start();
            System.Media.SystemSounds.Asterisk.Play();

            if (_cancelSignal != null) StartCancelListener();
        }

        private void StartCancelListener()
        {
            Thread t = new Thread(delegate()
            {
                try { _cancelSignal.WaitOne(); }
                catch { return; }
                try
                {
                    BeginInvoke(new MethodInvoker(delegate() { RequestClickFromTaskbar(); }));
                }
                catch { }
            });
            t.IsBackground = true;
            t.Name = "power-cancel-listener";
            t.Start();
        }

        // 任务栏按钮又被点了一下。间隔够短就当双击（直接开电源菜单），
        // 慢慢再点一次仍然只是取消——和卡片的 Cancel 按钮语义一致。
        private void RequestClickFromTaskbar()
        {
            if (_finished || _cancelling) return;

            if (!_preview && _watch.Elapsed.TotalMilliseconds < SystemInformation.DoubleClickTime)
            {
                _cancelling = true;
                _timer.Stop();
                _finished = true;
                DoubleClicked = true;
                Close();
                return;
            }

            RequestCancel();
        }

        private void OnTick(object sender, EventArgs e)
        {
            double elapsed = _watch.Elapsed.TotalSeconds;
            double fraction = 1.0 - elapsed / _seconds;
            if (fraction < 0) fraction = 0;

            int left = (int)Math.Ceiling(_seconds - elapsed);
            if (left < 0) left = 0;

            _ring.Update(left, fraction);

            if (elapsed >= _seconds)
            {
                _finished = true;
                _timer.Stop();
                Expired = true;
                Close();
            }
        }

        private void OnCancelClick(object sender, EventArgs e)
        {
            RequestCancel();
        }

        private void OnMenuClick(object sender, EventArgs e)
        {
            if (_finished || _cancelling) return;
            _cancelling = true;
            _timer.Stop();
            _finished = true;
            OpenMenuRequested = true;
            Close();
        }

        private void RequestCancel()
        {
            if (_finished || _cancelling) return;
            _cancelling = true;
            _timer.Stop();

            // 此刻还没有任何关机被排入系统，取消就是关掉这个窗口。
            _finished = true;
            System.Media.SystemSounds.Beep.Play();
            Close();
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            // 窗口被意外关闭（Alt+F4 等）等于取消：什么都不会发生。
            if (!_finished && !_preview) _finished = true;
        }
    }

    internal sealed class MenuForm : Form
    {
        internal PowerAction? PendingAction;
        internal int TimerMinutes;

        private readonly int _delay;
        private readonly Font _itemFont = new Font(L.FontFamily, 10f);

        internal MenuForm(int delay)
        {
            _delay = delay;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(32, 32, 32);
            KeyPreview = true;

            string[] labels = new string[] {
                L.T("menu.sleep"),
                L.T("menu.restart"),
                L.T("menu.shutdown"),
                L.T("menu.signout"),
                L.T("menu.lock"),
                L.T("menu.displayoff"),
                L.T("menu.timer"),
                L.T("menu.cancel"),
                L.T("menu.delay")
            };

            // 日文/英文条目长度差很多，宽度按最长一条量出来，避免文字被截断。
            int textWidth = 0;
            for (int i = 0; i < labels.Length; i++)
            {
                Size need = TextRenderer.MeasureText(labels[i], _itemFont);
                if (need.Width > textWidth) textWidth = need.Width;
            }

            int itemWidth = Math.Max(Ui.P(212), textWidth + Ui.P(40));
            ClientSize = new Size(itemWidth + Ui.P(20), Ui.P(9 * 38 + 14));

            for (int i = 0; i < labels.Length; i++)
            {
                Button b = new Button();
                b.Text = labels[i];
                b.Tag = i;
                b.SetBounds(Ui.P(10), Ui.P(7 + i * 38), itemWidth, Ui.P(34));
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderSize = 0;
                b.FlatAppearance.MouseOverBackColor = Color.FromArgb(62, 62, 62);
                b.BackColor = Color.FromArgb(32, 32, 32);
                b.ForeColor = Color.White;
                b.Font = _itemFont;
                b.TextAlign = ContentAlignment.MiddleLeft;
                b.Padding = new Padding(Ui.P(12), 0, 0, 0);
                b.Cursor = Cursors.Hand;
                b.Click += OnItemClick;
                Controls.Add(b);
            }

            Load += OnLoad;
            Deactivate += delegate { Close(); };
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) Close();
            };
        }

        protected override bool ShowWithoutActivation
        {
            get { return false; }
        }

        private void OnLoad(object sender, EventArgs e)
        {
            using (GraphicsPath path = Drawing.RoundedPath(ClientRectangle, Ui.P(10)))
            {
                Region = new Region(path);
            }

            Point cursor = Cursor.Position;
            Rectangle work = Screen.FromPoint(cursor).WorkingArea;
            Point slot = Taskbar.SlotFor(Size);
            int x = slot.X;
            int y = slot.Y;

            if (x + Width > work.Right) x = work.Right - Width - Ui.P(6);
            if (y + Height > work.Bottom) y = work.Bottom - Height - Ui.P(6);
            if (x < work.Left) x = work.Left + Ui.P(6);
            if (y < work.Top) y = work.Top + Ui.P(6);

            Location = new Point(x, y);
            Activate();
        }

        private void OnItemClick(object sender, EventArgs e)
        {
            int index = (int)((Button)sender).Tag;

            switch (index)
            {
                case 0:
                    Hide();
                    Application.SetSuspendState(PowerState.Suspend, false, false);
                    Close();
                    break;

                case 1:
                    PendingAction = PowerAction.Restart;
                    Close();
                    break;

                case 2:
                    PendingAction = PowerAction.Shutdown;
                    Close();
                    break;

                case 3:
                    Native.RunShutdown("/l");
                    Close();
                    break;

                case 4:
                    Native.LockWorkStation();
                    Close();
                    break;

                case 5:
                    Native.MonitorOff();
                    Close();
                    break;

                case 6:
                    ShowTimerMenu((Button)sender);
                    break;

                case 7:
                    if (!Native.AbortShutdown())
                        MessageDlg.Info(L.T("msg.nothingPending"));
                    Close();
                    break;

                case 8:
                    ShowDelayMenu((Button)sender);
                    break;
            }
        }

        private void ShowTimerMenu(Button owner)
        {
            int[] minutes = new int[] { 15, 30, 60, 120 };
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Font = new Font(L.FontFamily, 9f);

            for (int i = 0; i < minutes.Length; i++)
            {
                int value = minutes[i];
                ToolStripMenuItem item = new ToolStripMenuItem(L.TimerItem(value));
                item.Click += delegate
                {
                    TimerMinutes = value;
                    menu.Close();
                    Close();
                };
                menu.Items.Add(item);
            }

            menu.Show(owner, new Point(owner.Width + 2, 0));
        }

        private void ShowDelayMenu(Button owner)
        {
            int[] seconds = new int[] { 3, 5, 8, 10, 15 };
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Font = new Font(L.FontFamily, 9f);

            for (int i = 0; i < seconds.Length; i++)
            {
                int value = seconds[i];
                ToolStripMenuItem item = new ToolStripMenuItem(L.DelayItem(value, value == _delay));
                item.Click += delegate
                {
                    Config.Delay = value;
                    menu.Close();
                    Close();
                };
                menu.Items.Add(item);
            }

            menu.Show(owner, new Point(owner.Width + 2, 0));
        }
    }

    // 逻辑像素 -> 物理像素。进程声明了 DPI 感知，字体由 GDI+ 按 DPI 渲染，
    // 这里只负责把布局尺寸放大到当前缩放比例，避免 150% 下窗口偏小。
    internal static class Ui
    {
        private static float _scale = -1f;

        internal static float Scale
        {
            get
            {
                if (_scale > 0) return _scale;
                _scale = 1f;
                try
                {
                    using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                    {
                        _scale = g.DpiX / 96f;
                    }
                }
                catch { }
                if (_scale < 1f) _scale = 1f;
                if (_scale > 3f) _scale = 3f;
                return _scale;
            }
        }

        internal static int P(int value)
        {
            return (int)Math.Round(value * Scale);
        }

        internal static float F(float value)
        {
            return value * Scale;
        }
    }

    internal static class Drawing
    {
        internal static GraphicsPath RoundedPath(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;

            if (d <= 0 || r.Width <= d || r.Height <= d)
            {
                path.AddRectangle(r);
                return path;
            }

            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal static class Program
    {
        private const string CountdownMutexName = "Local\\PowerButton.Countdown";
        private const string MenuMutexName = "Local\\PowerButton.Menu";
        private const string CancelEventName = "Local\\PowerButton.Countdown.Cancel";

        internal static readonly string BaseDir = AppDomain.CurrentDomain.BaseDirectory;

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 语言要先定：下面任何一条错误提示都要用对语言。
            string langOverride = null;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();
                if ((a == "--lang" || a == "/lang") && i + 1 < args.Length)
                {
                    langOverride = args[i + 1];
                    break;
                }
            }

            L.Init(langOverride);

            try
            {
                Run(args);
            }
            catch (Exception ex)
            {
                MessageDlg.Error(L.T("err.generic") + ex.Message);
            }
        }

        private static void Run(string[] args)
        {
            bool forceMenu = false;
            bool preview = false;
            int delayOverride = -1;
            int previewSeconds = -1;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();

                if (a == "--menu" || a == "/menu")
                {
                    forceMenu = true;
                }
                else if (a == "--preview" || a == "/preview")
                {
                    preview = true;
                    if (i + 1 < args.Length)
                    {
                        int pv;
                        if (int.TryParse(args[i + 1], out pv) && pv > 0)
                        {
                            previewSeconds = pv;
                            i++;
                        }
                    }
                }
                else if (a == "--cancel" || a == "/cancel")
                {
                    Native.AbortShutdown();
                    return;
                }
                else if (a == "--selftest" || a == "/selftest")
                {
                    // 只检查能不能取到关机权限，不执行任何关机动作。
                    Environment.ExitCode = Native.CanEnableShutdownPrivilege() ? 0 : 2;
                    return;
                }
                else if (a == "--set-description" || a == "/set-description")
                {
                    // 安装脚本用：把中文备注写进 .lnk（WScript.Shell 会写坏成 '?'）。
                    // 文本可以直接给，也可以用 --file <utf8 文件> 传，避开命令行里的空格拆分。
                    if (i + 2 >= args.Length)
                    {
                        Environment.ExitCode = 2;
                        return;
                    }

                    string lnk = args[i + 1];
                    string text = args[i + 2];
                    if (text == "--file")
                    {
                        if (i + 3 >= args.Length)
                        {
                            Environment.ExitCode = 2;
                            return;
                        }

                        text = File.ReadAllText(args[i + 3], Encoding.UTF8).TrimEnd('\r', '\n');
                    }

                    Environment.ExitCode = Shortcut.SetDescription(lnk, text) ? 0 : 1;
                    return;
                }
                else if (a == "--label-out" || a == "/label-out")
                {
                    // 安装脚本用：把某条界面文案按当前语言写成文本文件（UTF-8，不带 BOM）。
                    // 用途：.lnk 备注、右键动词名——这些字符串不能硬编码在脚本里。
                    if (i + 2 >= args.Length)
                    {
                        Environment.ExitCode = 2;
                        return;
                    }

                    File.WriteAllText(args[i + 2], L.T(args[i + 1]), new UTF8Encoding(false));
                    Environment.ExitCode = 0;
                    return;
                }
                else if (a == "--lnk-probe" || a == "/lnk-probe")
                {
                    // 诊断：把 .lnk 的备注原样读出来写成文本文件（winexe 没有 stdout）。
                    if (i + 2 >= args.Length)
                    {
                        Environment.ExitCode = 2;
                        return;
                    }

                    string got = Shortcut.ReadDescription(args[i + 1]);
                    StringBuilder dump = new StringBuilder();
                    dump.AppendLine("length=" + got.Length);
                    dump.AppendLine("text=[" + got + "]");
                    StringBuilder points = new StringBuilder();
                    for (int k = 0; k < got.Length; k++)
                        points.Append((int)got[k]).Append(',');
                    dump.AppendLine("points=" + points);
                    File.WriteAllText(args[i + 2], dump.ToString(), new UTF8Encoding(false));
                    Environment.ExitCode = got.StartsWith("<error") ? 1 : 0;
                    return;
                }
                else if (a == "--delay" || a == "/delay")
                {
                    if (i + 1 < args.Length)
                    {
                        int v;
                        if (int.TryParse(args[i + 1], out v)) delayOverride = v;
                        i++;
                    }
                }
            }

            int delay = delayOverride >= 0 ? delayOverride : Config.Delay;
            if (preview && previewSeconds > 0) delay = previewSeconds;
            if (delay < 0) delay = 0;
            if (delay > 120) delay = 120;

            if (!preview && ((Control.ModifierKeys & Keys.Shift) == Keys.Shift))
                forceMenu = true;

            if (forceMenu)
            {
                RunMenu(delay);
                return;
            }

            RunCountdown(PowerAction.Shutdown, delay, preview);
        }

        private static void RunMenu(int delay)
        {
            bool owned;
            Mutex guard = new Mutex(true, MenuMutexName, out owned);

            if (!owned)
            {
                guard.Dispose();
                return;
            }

            PowerAction? action = null;
            int timerMinutes = 0;

            using (MenuForm form = new MenuForm(delay))
            {
                Application.Run(form);
                action = form.PendingAction;
                timerMinutes = form.TimerMinutes;
            }

            try { guard.ReleaseMutex(); }
            catch { }
            guard.Dispose();

            if (timerMinutes > 0)
            {
                Native.RunShutdown("/sg /t " + (timerMinutes * 60).ToString());
                MessageDlg.Info(L.ScheduledInfo(timerMinutes));
                return;
            }

            if (action.HasValue)
                RunCountdown(action.Value, delay, false);
        }

        private static void RunCountdown(PowerAction action, int seconds, bool preview)
        {
            bool owned;
            Mutex guard = new Mutex(true, CountdownMutexName, out owned);

            if (!owned)
            {
                // Second click while a countdown is running = cancel.
                guard.Dispose();
                RequestCancelFromOutside();
                return;
            }

            EventWaitHandle signal = null;
            try
            {
                signal = new EventWaitHandle(false, EventResetMode.AutoReset, CancelEventName);
            }
            catch
            {
                signal = null;
            }

            // 先等一个双击窗口：第二下落在窗口内就是「双击 = 开菜单」，
            // 这时卡片一眼都不该出现——否则会先闪一下「即将关机」再变菜单。
            // 窗口过了才建卡片，所以倒计时本身仍是完整的 seconds 秒。
            int clickWindow = SystemInformation.DoubleClickTime;
            if (!preview && signal != null && clickWindow > 0 && signal.WaitOne(clickWindow))
            {
                ReleaseGuard(guard, signal);
                RunMenu(Config.Delay);
                return;
            }

            if (!preview && seconds <= 0)
            {
                ReleaseGuard(guard, signal);
                Native.ExitShutdown(action == PowerAction.Restart);
                return;
            }

            CountdownForm form = null;
            try
            {
                form = new CountdownForm(action, seconds, signal, preview);
                Application.Run(form);
            }
            finally
            {
                ReleaseGuard(guard, signal);
            }

            // 只有倒计时真的走完才动手；被取消 / 窗口被关掉时什么都不做。
            if (form != null && form.OpenMenuRequested)
            {
                RunMenu(Config.Delay);
                return;
            }

            if (form != null && form.DoubleClicked)
            {
                RunMenu(Config.Delay);
                return;
            }

            if (form != null && form.Expired && !preview)
            {
                if (!Native.ExitShutdown(action == PowerAction.Restart))
                    Native.RunShutdown((action == PowerAction.Restart ? "/r" : "/sg") + " /t 0");
            }
        }

        private static void ReleaseGuard(Mutex guard, EventWaitHandle signal)
        {
            if (signal != null)
            {
                try { signal.Close(); }
                catch { }
            }

            try { guard.ReleaseMutex(); }
            catch { }
            guard.Dispose();
        }

        private static void RequestCancelFromOutside()
        {
            try
            {
                using (EventWaitHandle signal = EventWaitHandle.OpenExisting(CancelEventName))
                {
                    signal.Set();
                    return;
                }
            }
            catch { }

            // No live countdown window answered: make sure nothing is left pending.
            Native.AbortShutdown();
        }
    }
}
