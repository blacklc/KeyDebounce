// KeyDebounce -- portable tray launcher for KeyboardChatterBlocker.
//
// Why this exists: KeyboardChatterBlocker (the actual debounce engine) is a
// plain WinForms app with no single-instance guard, no portable config boot-
// strapping, and no visible "am I running?" indicator that survives a reboot.
// This launcher is a single self-contained .exe that lives next to the engine
// and gives you:
//   * a tray icon whose colour is the engine's real state (green = filtering,
//     red = not running),
//   * start / stop / restart of the engine without ever launching it twice
//     (two engines would install two keyboard hooks),
//   * a portable config.txt that is created next to the exe on first run,
//     so the whole folder can be copied to another PC as-is,
//   * a one-click "start with Windows" entry in the user's Startup folder.
//
// Build (no SDK needed, .NET Framework 4.x ships the compiler):
//   C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe ^
//     /out:..\KeyDebounce.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll KeyDebounce.cs
//
// C# 5 only -- the in-box csc.exe predates C# 6 syntax (no interpolation, no ?.).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

static class KeyDebounce
{
    const string EngineName  = "KeyboardChatterBlocker";          // process name, no .exe
    const string EngineExe   = "KeyboardChatterBlocker.exe";
    const string ConfigFile  = "config.txt";
    const string DocFile     = "USAGE.md";
    const string LogFile     = "KeyDebounce.log";
    const string ShortcutName = "KeyDebounce.lnk";

    // Written next to the exe when no config exists yet. KeyboardChatterBlocker
    // rewrites this file in its own order when it exits, so keep it simple.
    // Tuned from a raw-input capture of the user's CHERRY keyboard (046A:00E3):
    // one press of E produced three down/up pairs; every rebound landed
    // 4.7-10.3 ms after its release while genuine repeats were >= 95 ms apart.
    const string DefaultConfig =
        "# KeyboardChatterBlocker config (managed by KeyDebounce)\r\n" +
        "# Only the E key is filtered; every other key stays untouched.\r\n" +
        "is_enabled: true\r\n" +
        "global_chatter: 0\r\n" +
        "minimum_chatter_time: 0\r\n" +
        "key.E: 50\r\n" +
        "measure_from: Release\r\n" +
        // Deliberately no auto_disable_programs / auto_disable_on_fullscreen:
        // the filter must stay active from boot to shutdown, in every app.
        "hide_in_system_tray: true\r\n" +
        "save_stats: true\r\n" +
        "exclude_injected: false\r\n" +
        "hotkey_toggle: win + shift + k\r\n";

    static string dir;
    static string enginePath;
    static string configPath;
    static string logPath;

    static NotifyIcon tray;
    static ContextMenuStrip menu;
    static ToolStripMenuItem statusItem;
    static ToolStripMenuItem toggleItem;
    static ToolStripMenuItem autoItem;
    static System.Windows.Forms.Timer poll;   // Threading.Timer is also in scope
    static Icon iconRunning;
    static Icon iconStopped;
    static bool lastKnownRunning;
    static bool userPaused;                       // manual pause is respected
    static DateTime nextRestartAttempt = DateTime.MinValue;
    static int restartCount;

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool DestroyIcon(IntPtr hIcon);

    [STAThread]
    static void Main(string[] args)
    {
        L.SetFromArgs(args);   // --en / --zh override the auto-detected UI language

        // Tool windows run in their own short-lived process and deliberately
        // bypass the tray instance's mutex: no tray icon, no engine start.
        if (args.Length > 0)
        {
            string flag = args[0].ToLowerInvariant();
            if (flag == "--ui" || flag == "--manage" || flag == "--scan" ||
                flag == "--log" || flag == "--config")
            {
                string tab = flag == "--manage" ? "keys"
                           : flag == "--scan" ? "scan"
                           : flag == "--log" ? "log"
                           : flag == "--config" ? "config" : "ui";
                bool allowInjected = false;
                foreach (string a in args)
                    if (a.ToLowerInvariant() == "--allow-injected") allowInjected = true;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm(tab, allowInjected));
                return;
            }
        }

        bool isFirst;
        using (Mutex mutex = new Mutex(true, "KeyDebounce_SingleInstance_v1", out isFirst))
        {
            if (!isFirst)
            {
                MessageBox.Show(L.T("KeyDebounce已经在运行了。\r\n请查看屏幕右下角托盘里的图标（绿色 = 正在过滤）。"),
                    "KeyDebounce", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            dir = Path.GetDirectoryName(Application.ExecutablePath);
            enginePath = Path.Combine(dir, EngineExe);
            configPath = Path.Combine(dir, ConfigFile);
            logPath = Path.Combine(dir, LogFile);

            if (!File.Exists(enginePath))
            {
                MessageBox.Show(L.T("找不到 ") + EngineExe + L.T("。\r\n请把它和本程序放在同一个文件夹里。"),
                    "KeyDebounce", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Log(L.T("=== KeyDebounce 启动，目录 ") + dir + " ===");
            EnsureConfig();

            iconRunning = MakeIcon(true);
            iconStopped = MakeIcon(false);

            StartEngine(false);
            BuildTray();
            lastKnownRunning = EngineRunning();

            poll = new System.Windows.Forms.Timer();
            poll.Interval = 1000;
            poll.Tick += PollTick;
            poll.Start();

            Application.Run();

            poll.Stop();
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            Log(L.T("=== KeyDebounce 退出 ==="));
        }
    }

    // ---------- engine control ----------

    static bool EngineRunning()
    {
        try { return Process.GetProcessesByName(EngineName).Length > 0; }
        catch { return false; }
    }

    static void StartEngine(bool quiet)
    {
        if (EngineRunning())
        {
            if (!quiet) Log(L.T("引擎已在运行，不重复启动（重复启动会装第二个键盘钩子）"));
            return;
        }
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo(enginePath);
            psi.WorkingDirectory = dir;   // engine keeps config.txt next to itself
            psi.UseShellExecute = true;
            Process p = Process.Start(psi);
            Log(L.T("已启动引擎，pid=") + p.Id);
            if (!quiet) Balloon(L.T("KeyDebounce已启动"), L.T("引擎正在运行：只过滤 E 键（50ms），其它按键不受影响。"));
        }
        catch (Exception ex)
        {
            Log(L.T("启动引擎失败: ") + ex.Message);
            MessageBox.Show(L.T("启动引擎失败：") + ex.Message, "KeyDebounce",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    static void StopEngine()
    {
        try
        {
            foreach (Process p in Process.GetProcessesByName(EngineName))
            {
                p.Kill();
                p.WaitForExit(3000);
                Log(L.T("已停止引擎，pid=") + p.Id);
            }
        }
        catch (Exception ex) { Log(L.T("停止引擎失败: ") + ex.Message); }
        UpdateStatus(true);
    }

    // ---------- config ----------

    static void EnsureConfig()
    {
        if (!File.Exists(configPath))
        {
            File.WriteAllText(configPath, DefaultConfig, new UTF8Encoding(false));
            Log(L.T("已生成 config.txt（E 键 50ms / measure_from: Release）"));
            return;
        }

        try
        {
            List<string> lines = new List<string>(File.ReadAllLines(configPath));
            bool changed = false;
            bool sawKeyE = false;

            for (int i = 0; i < lines.Count; i++)
            {
                string t = lines[i].Trim();
                if (t.StartsWith("is_enabled:", StringComparison.OrdinalIgnoreCase) &&
                    t.IndexOf("true", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    lines[i] = "is_enabled: true";
                    changed = true;
                    Log(L.T("config.txt: is_enabled 原本是关的，已改为 true"));
                }
                if (t.StartsWith("key.E:", StringComparison.OrdinalIgnoreCase)) sawKeyE = true;
            }

            if (!sawKeyE)
            {
                lines.Add("key.E: 50");
                changed = true;
                Log(L.T("config.txt: 缺少 key.E 条目，已补上 50ms"));
            }

            if (changed) File.WriteAllLines(configPath, lines.ToArray(), new UTF8Encoding(false));
        }
        catch (Exception ex) { Log(L.T("读取 config.txt 失败: ") + ex.Message); }
    }

    static string CurrentThreshold()
    {
        try
        {
            foreach (string line in File.ReadAllLines(configPath))
            {
                string t = line.Trim();
                if (t.StartsWith("key.E:", StringComparison.OrdinalIgnoreCase))
                    return t.Substring(6).Trim();
            }
        }
        catch { }
        return "?";
    }

    // ---------- tray ----------

    static void BuildTray()
    {
        menu = new ContextMenuStrip();

        statusItem = new ToolStripMenuItem(L.T("状态：检查中…"));
        statusItem.Enabled = false;
        menu.Items.Add(statusItem);
        menu.Items.Add(new ToolStripSeparator());

        toggleItem = new ToolStripMenuItem(L.T("暂停过滤（停止引擎）"));
        toggleItem.Click += delegate
        {
            if (EngineRunning()) { userPaused = true; StopEngine(); }
            else { userPaused = false; StartEngine(false); }
        };
        menu.Items.Add(toggleItem);

        ToolStripMenuItem restart = new ToolStripMenuItem(L.T("重启引擎"));
        restart.Click += delegate
        {
            userPaused = false;
            StopEngine();
            Thread.Sleep(400);
            StartEngine(true);
        };
        menu.Items.Add(restart);

        autoItem = new ToolStripMenuItem(L.T("开机自启"));
        autoItem.CheckOnClick = false;
        autoItem.Click += delegate
        {
            try
            {
                bool want = !AutostartEnabled();
                SetAutostart(want);
                Log(want ? L.T("已开启开机自启") : L.T("已关闭开机自启"));
            }
            catch (Exception ex)
            {
                Log(L.T("设置开机自启失败: ") + ex.Message);
                MessageBox.Show(L.T("设置开机自启失败：") + ex.Message, "KeyDebounce",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            UpdateStatus(true);
        };
        menu.Items.Add(autoItem);
        menu.Items.Add(new ToolStripSeparator());

        ToolStripMenuItem openCfg = new ToolStripMenuItem(L.T("打开配置文件（改阈值）"));
        openCfg.Click += delegate { Open(configPath); };
        menu.Items.Add(openCfg);

        ToolStripMenuItem openDoc = new ToolStripMenuItem(L.T("打开使用说明"));
        openDoc.Click += delegate
        {
            string d = Path.Combine(dir, DocFile);
            if (File.Exists(d)) Open(d);
            else MessageBox.Show(L.T("找不到 ") + DocFile, "KeyDebounce");
        };
        menu.Items.Add(openDoc);

        ToolStripMenuItem openDir = new ToolStripMenuItem(L.T("打开安装文件夹"));
        openDir.Click += delegate { Open(dir); };
        menu.Items.Add(openDir);
        menu.Items.Add(new ToolStripSeparator());

        ToolStripMenuItem openUi = new ToolStripMenuItem(L.T("打开主界面（配置 / 日志 / 扫描）"));
        openUi.Font = new Font(openUi.Font, FontStyle.Bold);
        openUi.Click += delegate { OpenUi("--ui"); };
        menu.Items.Add(openUi);

        ToolStripMenuItem openLogMenu = new ToolStripMenuItem(L.T("查看实时日志"));
        openLogMenu.Click += delegate { OpenUi("--log"); };
        menu.Items.Add(openLogMenu);
        menu.Items.Add(new ToolStripSeparator());

        ToolStripMenuItem about = new ToolStripMenuItem(L.T("关于 / 当前状态"));
        about.Click += delegate { ShowAbout(); };
        menu.Items.Add(about);

        ToolStripMenuItem quit = new ToolStripMenuItem(L.T("退出"));
        quit.Click += delegate
        {
            if (EngineRunning())
            {
                DialogResult r = MessageBox.Show(
                    L.T("同时停止键盘过滤引擎吗？\r\n\r\n是 = 关掉过滤，退出本程序（键盘抖动会重新出现）\r\n否 = 保留引擎继续过滤，只退出托盘程序"),
                    L.T("退出 KeyDebounce"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel) return;
                if (r == DialogResult.Yes) { userPaused = true; StopEngine(); }
            }
            Application.Exit();
        };
        menu.Items.Add(quit);

        tray = new NotifyIcon();
        tray.Icon = iconStopped;
        tray.Text = L.T("KeyDebounce：正在检查…");
        tray.ContextMenuStrip = menu;
        tray.Visible = true;
        tray.DoubleClick += delegate { OpenUi("--ui"); };
        tray.BalloonTipTitle = "KeyDebounce";
        tray.BalloonTipText = L.T("已启动。绿色图标 = 正在过滤 E 键抖动。");
        tray.ShowBalloonTip(4000);

        UpdateStatus(true);
    }

    static void PollTick(object sender, EventArgs e)
    {
        bool running = EngineRunning();

        // A tool window (主界面/扫描) restarting the engine on purpose writes
        // this marker; stay out of its way or we would start a second engine.
        bool restarting = false;
        try { restarting = File.Exists(Path.Combine(dir, ".restarting")); }
        catch { }

        if (running != lastKnownRunning)
        {
            lastKnownRunning = running;
            Log(running ? L.T("检测到引擎在运行") : L.T("检测到引擎已停止"));
            if (!running && !restarting)
                Balloon(L.T("过滤已停止"), L.T("KeyboardChatterBlocker 不再运行，正在尝试恢复。"));
        }

        // Requirement: the filter must live from boot to shutdown. If the engine
        // dies (crash, user kill, antivirus) bring it back -- unless the user
        // deliberately paused it, or a tool window is restarting it right now.
        if (!running && !userPaused && !restarting && DateTime.Now >= nextRestartAttempt)
        {
            nextRestartAttempt = DateTime.Now.AddSeconds(10);
            restartCount++;
            Log(L.T("引擎未运行且未手动暂停 -> 自动重启（第 ") + restartCount + L.T(" 次）"));
            StartEngine(true);
            UpdateStatus(true);
            return;
        }

        UpdateStatus(false);
    }

    static void UpdateStatus(bool force)
    {
        bool running = EngineRunning();
        lastKnownRunning = running;

        tray.Icon = running ? iconRunning : iconStopped;
        string tip = running
            ? L.T("KeyDebounce：正在过滤（阈值 ") + CurrentThreshold() + L.T("ms，仅 E 键）")
            : L.T("KeyDebounce：未过滤（引擎没在运行）");
        // NotifyIcon.Text is capped at 63 chars by the shell
        if (tip.Length > 62) tip = tip.Substring(0, 62);
        tray.Text = tip;

        statusItem.Text = running
            ? L.T("状态：正在过滤（阈值 ") + CurrentThreshold() + L.T("ms）")
            : L.T("状态：未过滤（引擎未运行）");
        toggleItem.Text = running ? L.T("暂停过滤（停止引擎）") : L.T("开始过滤（启动引擎）");
        autoItem.Checked = AutostartEnabled();
        WriteStatus(running);
        if (force) { }
    }

    // Machine-readable proof of the live state: what the tray icon is showing
    // right now, which engine pid backs it, and the threshold in force.
    static void WriteStatus(bool running)
    {
        try
        {
            string s = "e_key_filter=" + (running ? "running" : "stopped") + "\r\n" +
                       "tray_icon=" + (running ? "green" : "red") + "\r\n" +
                       "threshold_ms=" + CurrentThreshold() + "\r\n" +
                       "autostart=" + (AutostartEnabled() ? "on" : "off") + "\r\n" +
                       "updated=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            File.WriteAllText(Path.Combine(dir, "KeyDebounce.status"), s, new UTF8Encoding(false));
        }
        catch { }
    }

    static void Balloon(string title, string text)
    {
        if (tray == null) return;
        tray.BalloonTipTitle = title;
        tray.BalloonTipText = text;
        tray.ShowBalloonTip(4000);
    }

    static void ShowAbout()
    {
        bool running = EngineRunning();
        string s =
            "KeyDebounce " + (running ? L.T("正在过滤") : L.T("未在过滤")) + "\r\n\r\n" +
            L.T("引擎：KeyboardChatterBlocker（E 键 ") + CurrentThreshold() + L.T("ms，") +
            L.T("measure_from: Release）\r\n") +
            L.T("配置：") + configPath + "\r\n" +
            L.T("开机自启：") + (AutostartEnabled() ? L.T("已开启") : L.T("未开启")) + "\r\n" +
            L.T("托盘图标：绿色 = 过滤中，红色 = 已停止\r\n\r\n") +
            L.T("内核驱动、注册表、服务：都没有改动。删除本文件夹即可彻底移除。");
        MessageBox.Show(s, "KeyDebounce", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    static void Open(string pathOrDir)
    {
        try
        {
            ProcessStartInfo psi;
            if (pathOrDir.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || pathOrDir.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                psi = new ProcessStartInfo("notepad.exe", "\"" + pathOrDir + "\"");
            else
                psi = new ProcessStartInfo(pathOrDir);
            psi.UseShellExecute = true;
            Process.Start(psi);
        }
        catch (Exception ex) { Log(L.T("打开失败 ") + pathOrDir + ": " + ex.Message); }
    }

    /// Opens one of our own tool windows (--ui / --log / --keys ...) as a
    /// separate process, so the tray instance keeps its single-instance mutex.
    static void OpenUi(string arg)
    {
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath, arg);
            psi.WorkingDirectory = dir;
            psi.UseShellExecute = true;
            Process.Start(psi);
        }
        catch (Exception ex) { Log(L.T("打开主界面失败: ") + ex.Message); }
    }

    // ---------- start with Windows ----------

    static string ShortcutPath()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), ShortcutName);
    }

    static bool AutostartEnabled()
    {
        try { return File.Exists(ShortcutPath()); }
        catch { return false; }
    }

    static void SetAutostart(bool on)
    {
        string lnk = ShortcutPath();
        if (!on)
        {
            if (File.Exists(lnk)) File.Delete(lnk);
            return;
        }

        object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        // reflection instead of `dynamic` so no Microsoft.CSharp reference is needed
        object sc = shell.GetType().InvokeMember("CreateShortcut",
            BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
        Type t = sc.GetType();
        t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc,
            new object[] { Application.ExecutablePath });
        t.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc,
            new object[] { dir });
        t.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc,
            new object[] { Application.ExecutablePath + ",0" });
        t.InvokeMember("Description", BindingFlags.SetProperty, null, sc,
            new object[] { L.T("KeyDebounce（键盘抖动过滤）") });
        t.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
    }

    // ---------- misc ----------

    static Icon MakeIcon(bool running)
    {
        using (Bitmap bmp = new Bitmap(32, 32))
        {
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                Color fill = running ? Color.FromArgb(46, 160, 67) : Color.FromArgb(170, 60, 60);
                using (SolidBrush b = new SolidBrush(fill))
                    g.FillEllipse(b, 1, 1, 30, 30);
                using (Pen p = new Pen(Color.FromArgb(230, 255, 255, 255), 2f))
                    g.DrawEllipse(p, 1, 1, 30, 30);
                using (Font f = new Font("Segoe UI", 17f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (SolidBrush tb = new SolidBrush(Color.White))
                {
                    StringFormat sf = new StringFormat();
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    g.DrawString("E", f, tb, new RectangleF(0, 0, 32, 31), sf);
                }
            }
            IntPtr h = bmp.GetHicon();
            try { return (Icon)Icon.FromHandle(h).Clone(); }
            finally { DestroyIcon(h); }
        }
    }

    static void Log(string message)
    {
        try
        {
            File.AppendAllText(logPath,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + "\r\n",
                new UTF8Encoding(false));
        }
        catch { }
    }
}
