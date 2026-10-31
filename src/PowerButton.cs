// PowerButton - a safer replacement for the taskbar "shutdown" shortcut.
// Left click  : countdown window, cancellable; shuts down only when it reaches 0
// Click again : cancels the pending countdown
// Shift+click : power menu, also available as a right-click verb
// No logging, no telemetry, no admin rights required.
//
// Compiled with the .NET Framework 4.x csc.exe (C# 5):
//   csc /target:winexe /codepage:65001 /win32icon:power.ico /out:PowerButton.exe PowerButton.cs

using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace PowerButton
{
    internal enum PowerAction
    {
        Shutdown,
        Restart
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
                MessageDlg.Error("无法调用 shutdown.exe：" + ex.Message);
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

    internal static class MessageDlg
    {
        internal static void Info(string text)
        {
            MessageBox.Show(text, "电源", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        internal static void Error(string text)
        {
            MessageBox.Show(text, "电源", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // Tiny key=value store next to the executable: %LOCALAPPDATA%\PowerButton\config.ini
    internal static class Config
    {
        private static readonly string Path =
            System.IO.Path.Combine(Program.BaseDir, "config.ini");

        private static int _delay = -1;

        internal static int Delay
        {
            get
            {
                if (_delay > 0) return _delay;
                _delay = 5;
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
                            if (!line.Substring(0, eq).Trim().Equals("delay", StringComparison.OrdinalIgnoreCase))
                                continue;
                            int v;
                            if (int.TryParse(line.Substring(eq + 1).Trim(), out v) && v >= 0 && v <= 120)
                                _delay = v;
                        }
                    }
                }
                catch { }
                return _delay;
            }
            set
            {
                int v = value;
                if (v < 0) v = 0;
                if (v > 120) v = 120;
                _delay = v;
                try
                {
                    File.WriteAllLines(Path, new string[] {
                        "# 电源按钮设置",
                        "# delay = 左键单击后等待的秒数，0 表示立即执行（不建议）",
                        "delay=" + v.ToString()
                    });
                }
                catch { }
            }
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

        private bool _finished;
        private bool _cancelling;

        // 倒计时走完（而不是被取消）时为 true：由调用方在这之后真正执行关机。
        internal bool Expired;

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
            Text = "电源";

            _ring.SetBounds(Ui.P(18), Ui.P(20), Ui.P(84), Ui.P(84));

            _caption.SetBounds(Ui.P(116), Ui.P(22), Ui.P(248), Ui.P(30));
            _caption.Font = new Font("Microsoft YaHei UI", 13.5f, FontStyle.Bold);
            _caption.ForeColor = Color.White;
            _caption.BackColor = Color.Transparent;
            _caption.Text = preview ? "预览（不会执行）"
                : (action == PowerAction.Restart ? "即将重启" : "即将关机");

            _detail.SetBounds(Ui.P(116), Ui.P(56), Ui.P(248), Ui.P(40));
            _detail.Font = new Font("Microsoft YaHei UI", 9f);
            _detail.ForeColor = Color.FromArgb(168, 168, 168);
            _detail.BackColor = Color.Transparent;
            _detail.Text = preview
                ? "仅测试界面，不会关机"
                : "再次点击任务栏按钮可取消";

            _cancel.SetBounds(Ui.P(18), Ui.P(112), Ui.P(344), Ui.P(34));
            _cancel.FlatStyle = FlatStyle.Flat;
            _cancel.FlatAppearance.BorderSize = 0;
            _cancel.FlatAppearance.MouseOverBackColor = Color.FromArgb(198, 12, 28);
            _cancel.BackColor = Color.FromArgb(232, 17, 35);
            _cancel.ForeColor = Color.White;
            _cancel.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
            _cancel.Text = preview ? "关闭预览"
                : (action == PowerAction.Restart ? "取消重启" : "取消关机");
            _cancel.Cursor = Cursors.Hand;
            _cancel.Click += OnCancelClick;

            Controls.Add(_ring);
            Controls.Add(_caption);
            Controls.Add(_detail);
            Controls.Add(_cancel);

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

            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(work.Right - Width - Ui.P(20), work.Bottom - Height - Ui.P(12));

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
                    BeginInvoke(new MethodInvoker(delegate() { RequestCancel(); }));
                }
                catch { }
            });
            t.IsBackground = true;
            t.Name = "power-cancel-listener";
            t.Start();
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
        private readonly Font _itemFont = new Font("Microsoft YaHei UI", 10f);

        internal MenuForm(int delay)
        {
            _delay = delay;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(32, 32, 32);
            ClientSize = new Size(Ui.P(232), Ui.P(9 * 38 + 14));
            KeyPreview = true;

            string[] labels = new string[] {
                "睡眠",
                "重启",
                "关机",
                "注销",
                "锁定",
                "关闭显示器",
                "定时关机…",
                "取消关机",
                "倒计时设置…"
            };

            for (int i = 0; i < labels.Length; i++)
            {
                Button b = new Button();
                b.Text = labels[i];
                b.Tag = i;
                b.SetBounds(Ui.P(10), Ui.P(7 + i * 38), Ui.P(212), Ui.P(34));
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
            int x = cursor.X - Ui.P(24);
            int y = cursor.Y - Ui.P(24);

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
                        MessageDlg.Info("当前没有等待执行的关机或重启。");
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
            menu.Font = new Font("Microsoft YaHei UI", 9f);

            for (int i = 0; i < minutes.Length; i++)
            {
                int value = minutes[i];
                ToolStripMenuItem item = new ToolStripMenuItem(value + " 分钟后关机");
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
            menu.Font = new Font("Microsoft YaHei UI", 9f);

            for (int i = 0; i < seconds.Length; i++)
            {
                int value = seconds[i];
                ToolStripMenuItem item = new ToolStripMenuItem(value + " 秒" + (value == _delay ? "（当前）" : ""));
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

            try
            {
                Run(args);
            }
            catch (Exception ex)
            {
                MessageDlg.Error("电源按钮出错：" + ex.Message);
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
                MessageDlg.Info("已安排在 " + timerMinutes + " 分钟后关机。\n\n" +
                                "要提前撤销，用右键菜单里的「取消关机」。");
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
