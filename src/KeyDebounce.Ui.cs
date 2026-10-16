// KeyTools -- the KeyDebounce main window and its panels.
//
// Launched as `KeyDebounce.exe --ui` (the tray icon opens it; --manage / --scan just
// preselect a tab). Everything the app can do lives in one window:
//
//   概览       live status, start/stop/restart, per-key intercept counters
//   过滤按键   add/remove keys and set per-key thresholds
//   抖动扫描   watch the RAW keyboard signal and find newly chattering keys
//   日志       live log: launcher events + every intercept with its gap in ms
//   配置       the raw config.txt, validated before saving
//
// Two design notes worth keeping in mind:
//
// 1. Key names. KeyboardChatterBlocker parses `key.<name>` with
//    Enum.TryParse(name, true, out Keys) plus six mouse pseudo-names, and an
//    unknown name makes it pop a MODAL ERROR at startup. So this file only ever
//    writes ((Keys)vkCode).ToString(), and it validates names before saving.
//    (Upstream KeyBlocker.cs / KeysHelper.cs are the authoritative reference.)
//
// 2. The intercept log. The engine writes blocker_stats.csv only when it saves
//    its own config (its SaveStatsTicker is never used), so that file cannot
//    drive a live view. Instead this app installs its own low-level hook AFTER
//    the engine's -- Windows runs hooks newest-first, so ours sees the stream
//    BEFORE the engine filters it -- and replays the engine's rule (per-key
//    threshold, measured from the last ACCEPTED press or release, with blocked
//    presses not resetting the clock) to count and log every suppressed bounce.
//
// C# 5 only (in-box csc.exe).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

// ----------------------------------------------------------------- config ---

static class Cfg
{
    public static string Dir { get { return Path.GetDirectoryName(Application.ExecutablePath); } }
    public static string ConfigPath { get { return Path.Combine(Dir, "config.txt"); } }
    public static string EnginePath { get { return Path.Combine(Dir, "KeyboardChatterBlocker.exe"); } }
    public static string LogPath { get { return Path.Combine(Dir, "KeyDebounce.log"); } }
    public static string StatsPath { get { return Path.Combine(Dir, "blocker_stats.csv"); } }
    public static string Marker { get { return Path.Combine(Dir, ".restarting"); } }

    /// Live intercept counters written by the tray launcher (KeyDebounce.cs).
    public static string MirrorStatsPath { get { return Path.Combine(Dir, "KeyDebounce.stats"); } }

    public static string StartupLnk
    {
        get
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "KeyDebounce.lnk");
        }
    }

    public static bool EngineRunning()
    {
        try { return Process.GetProcessesByName("KeyboardChatterBlocker").Length > 0; }
        catch { return false; }
    }

    public static int EnginePid()
    {
        try
        {
            Process[] ps = Process.GetProcessesByName("KeyboardChatterBlocker");
            if (ps.Length > 0) return ps[0].Id;
        }
        catch { }
        return 0;
    }

    public static List<KeyValuePair<string, string>> LoadKeys()
    {
        List<KeyValuePair<string, string>> list = new List<KeyValuePair<string, string>>();
        if (!File.Exists(ConfigPath)) return list;
        foreach (string raw in File.ReadAllLines(ConfigPath))
        {
            string t = raw.Trim();
            int c = t.IndexOf(':');
            if (t.Length == 0 || t[0] == '#' || c <= 0) continue;
            string name = t.Substring(0, c).Trim();
            string val = t.Substring(c + 1).Trim();
            if (name.StartsWith("key.", StringComparison.OrdinalIgnoreCase))
                list.Add(new KeyValuePair<string, string>(name.Substring(4), val));
        }
        return list;
    }

    /// How many keys are actually filtered (a key.* entry with a non-zero value).
    public static int FilteredKeyCount()
    {
        int n = 0;
        foreach (KeyValuePair<string, string> kv in LoadKeys())
        {
            int ms;
            if (int.TryParse(kv.Value, out ms) && ms > 0) n++;
        }
        return n;
    }

    public static string ReadSetting(string setting)
    {
        try
        {
            foreach (string raw in File.ReadAllLines(ConfigPath))
            {
                string t = raw.Trim();
                int c = t.IndexOf(':');
                if (t.Length == 0 || t[0] == '#' || c <= 0) continue;
                if (t.Substring(0, c).Trim().Equals(setting, StringComparison.OrdinalIgnoreCase))
                    return t.Substring(c + 1).Trim();
            }
        }
        catch { }
        return "";
    }

    /// Rewrites config.txt: every other setting is preserved verbatim, all
    /// key.* lines are replaced by the supplied set, global_chatter stays 0
    /// so unfiltered keys are never touched.
    public static void SaveKeys(List<KeyValuePair<string, string>> keys)
    {
        List<string> outLines = new List<string>();
        bool hasGlobal = false, hasEnabled = false;

        if (File.Exists(ConfigPath))
        {
            foreach (string raw in File.ReadAllLines(ConfigPath))
            {
                string t = raw.Trim();
                int c = t.IndexOf(':');
                string name = c > 0 ? t.Substring(0, c).Trim() : "";
                if (name.StartsWith("key.", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Equals("global_chatter", StringComparison.OrdinalIgnoreCase))
                {
                    outLines.Add("global_chatter: 0");
                    hasGlobal = true;
                    continue;
                }
                if (name.Equals("is_enabled", StringComparison.OrdinalIgnoreCase)) hasEnabled = true;
                outLines.Add(raw);
            }
        }
        if (!hasEnabled) outLines.Insert(0, "is_enabled: true");
        if (!hasGlobal) outLines.Add("global_chatter: 0");
        foreach (KeyValuePair<string, string> kv in keys)
            outLines.Add("key." + kv.Key + ": " + kv.Value);

        File.WriteAllLines(ConfigPath, outLines.ToArray(), new UTF8Encoding(false));
    }

    /// The engine reads config.txt only at startup, so a change needs a restart.
    /// The marker file keeps the tray launcher's self-heal from racing us and
    /// starting a second engine (two engines = two keyboard hooks).
    public static string RestartEngine()
    {
        try { File.WriteAllText(Marker, DateTime.Now.ToString("s"), new UTF8Encoding(false)); }
        catch { }
        try
        {
            foreach (Process p in Process.GetProcessesByName("KeyboardChatterBlocker"))
            {
                try { p.Kill(); p.WaitForExit(3000); }
                catch { }
            }
            System.Threading.Thread.Sleep(700);
            ProcessStartInfo psi = new ProcessStartInfo(EnginePath);
            psi.WorkingDirectory = Dir;
            psi.UseShellExecute = true;
            Process np = Process.Start(psi);
            System.Threading.Thread.Sleep(2500);
            return L.T("引擎已按新配置重启（PID ") + np.Id + L.T("）。");
        }
        catch (Exception ex)
        {
            return L.T("重启引擎失败：") + ex.Message;
        }
        finally
        {
            try { File.Delete(Marker); } catch { }
        }
    }

    public static string StopEngine()
    {
        int n = 0;
        foreach (Process p in Process.GetProcessesByName("KeyboardChatterBlocker"))
        {
            try { p.Kill(); p.WaitForExit(3000); n++; }
            catch { }
        }
        return n > 0 ? L.T("已停止 ") + n + L.T(" 个引擎进程。") : L.T("引擎本来就没在运行。");
    }

    public static string StartEngine()
    {
        if (EngineRunning()) return L.T("引擎已经在运行。");
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo(EnginePath);
            psi.WorkingDirectory = Dir;
            psi.UseShellExecute = true;
            Process p = Process.Start(psi);
            return L.T("引擎已启动（PID ") + p.Id + L.T("）。");
        }
        catch (Exception ex) { return L.T("启动失败：") + ex.Message; }
    }

    public static void AppendLog(string line)
    {
        try
        {
            FileInfo fi = new FileInfo(LogPath);
            if (fi.Exists && fi.Length > 2 * 1024 * 1024)
            {
                string previous = LogPath + ".1";
                if (File.Exists(previous)) File.Delete(previous);
                File.Move(LogPath, previous);
            }
            File.AppendAllText(LogPath,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + line + "\r\n",
                new UTF8Encoding(false));
        }
        catch { }
    }
}

// --------------------------------------------------------- low-level hook ---

class KeyboardObserver : IDisposable
{
    public delegate void KeyEvent(int vkCode, bool isDown, bool injected);
    public event KeyEvent Key;

    const int WH_KEYBOARD_LL = 13;
    const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101;
    const int WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    const int LLKHF_INJECTED = 0x10;

    delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct KbdLlHookStruct
    {
        public int vkCode;
        public int scanCode;
        public int flags;
        public int time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")]
    static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)]
    static extern bool UnhookWindowsHookEx(IntPtr hhk);

    IntPtr hook = IntPtr.Zero;
    HookProc proc;                 // must stay referenced or it gets collected

    public KeyboardObserver()
    {
        proc = Callback;
        hook = SetWindowsHookEx(WH_KEYBOARD_LL, proc, IntPtr.Zero, 0);
        if (hook == IntPtr.Zero)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }

    IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode == 0)
        {
            KbdLlHookStruct data = (KbdLlHookStruct)Marshal.PtrToStructure(lParam, typeof(KbdLlHookStruct));
            int msg = wParam.ToInt32();
            if (msg == WM_KEYDOWN || msg == WM_KEYUP || msg == WM_SYSKEYDOWN || msg == WM_SYSKEYUP)
            {
                bool isDown = (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN);
                bool injected = (data.flags & LLKHF_INJECTED) != 0;
                if (Key != null)
                {
                    try { Key(data.vkCode, isDown, injected); }
                    catch { }
                }
            }
        }
        return CallNextHookEx(hook, nCode, wParam, lParam);   // never block anything
    }

    public void Dispose()
    {
        if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
    }
}

// --------------------------------------------------------------- helpers ----

static class KeyNames
{
    public static string FromVk(int vk)
    {
        try { return ((Keys)vk).ToString(); }
        catch { return vk.ToString(); }
    }

    public static bool TryVk(string name, out int vk)
    {
        vk = 0;
        Keys k;
        if (Enum.TryParse<Keys>(name, true, out k) && (int)k != 0)
        {
            vk = (int)k;
            return true;
        }
        return false;
    }

    public static bool IsMouse(string name)
    {
        return name.StartsWith("mouse_", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("wheel_change", StringComparison.OrdinalIgnoreCase);
    }

    public static string Pretty(string name)
    {
        if (name == "mouse_left") return L.T("鼠标左键");
        if (name == "mouse_right") return L.T("鼠标右键");
        if (name == "mouse_middle") return L.T("鼠标中键");
        if (name == "mouse_forward") return L.T("鼠标前进键");
        if (name == "mouse_backward") return L.T("鼠标后退键");
        if (name == "wheel_change") return L.T("滚轮方向抖动");
        return name;
    }

    public static readonly string[] MouseChoices = new string[]
    {
        "mouse_left", "mouse_right", "mouse_middle", "mouse_forward",
        "mouse_backward", "wheel_change"
    };
}

// ------------------------------------------------------- intercept mirror ---

/// Replays the engine's rule on the pre-filter stream, so the UI can show live
/// which presses the engine is dropping and how far they bounced.
class ChatterMirror
{
    class KeyState
    {
        public double LastLoggedMs = -1e9;
    public double LastAcceptedDown = -1;
        public double LastAcceptedUp = -1;
        public bool PressAccepted;
        public int Presses, Blocked;
        public string LastBlockedTime = "";
    }

    const int LogLineCap = 200;      // stop flooding the log after this many

    Stopwatch clock = new Stopwatch();
    Dictionary<int, KeyState> state = new Dictionary<int, KeyState>();
    Dictionary<int, int> thresholds = new Dictionary<int, int>();
    string measureFrom = "Press";

    public bool Active;              // mirror only while the engine runs
    public bool IncludeInjected;     // debug only: also mirror injected keys
    public event Action<string> Line;

    public ChatterMirror() { clock.Start(); Reload(); }

    public void Reload()
    {
        thresholds.Clear();
        foreach (KeyValuePair<string, string> kv in Cfg.LoadKeys())
        {
            if (KeyNames.IsMouse(kv.Key)) continue;
            int vk, ms;
            if (!KeyNames.TryVk(kv.Key, out vk)) continue;
            if (!int.TryParse(kv.Value, out ms)) continue;
            if (ms > 0) thresholds[vk] = ms;
        }
        string mf = Cfg.ReadSetting("measure_from");
        measureFrom = string.IsNullOrEmpty(mf) ? "Press" : mf;
    }

    public int FilteredKeyCount { get { return thresholds.Count; } }

    public void Reset() { state.Clear(); }

    public List<string[]> Snapshot()
    {
        List<string[]> rows = new List<string[]>();
        foreach (KeyValuePair<int, KeyState> kv in state)
        {
            KeyState s = kv.Value;
            int thr;
            bool filtered = thresholds.TryGetValue(kv.Key, out thr);
            rows.Add(new string[]
            {
                KeyNames.Pretty(KeyNames.FromVk(kv.Key)),
                filtered ? thr + " ms" : L.T("不过滤"),
                s.Presses.ToString(),
                filtered ? s.Blocked.ToString() : "-",
                s.LastBlockedTime
            });
        }
        rows.Sort(delegate(string[] a, string[] b)
        {
            int x, y;
            int.TryParse(a[3], out x);
            int.TryParse(b[3], out y);
            return y.CompareTo(x);
        });
        return rows;
    }

    /// Raw values for the launcher's machine-readable stats file, as opposed to
    /// the localised display strings Snapshot() produces for the UI.
    public List<string[]> RawSnapshot()
    {
        List<string[]> rows = new List<string[]>();
        foreach (KeyValuePair<int, KeyState> kv in state)
        {
            KeyState s = kv.Value;
            int thr;
            bool filtered = thresholds.TryGetValue(kv.Key, out thr);
            rows.Add(new string[]
            {
                KeyNames.FromVk(kv.Key),
                filtered ? thr.ToString() : "0",
                s.Presses.ToString(),
                filtered ? s.Blocked.ToString() : "0",
                s.LastBlockedTime
            });
        }
        return rows;
    }

    public void OnKey(int vk, bool isDown, bool injected)
    {
        if (!Active) return;
        if (injected && !IncludeInjected) return;

        KeyState s;
        if (!state.TryGetValue(vk, out s)) { s = new KeyState(); state[vk] = s; }

        double now = clock.Elapsed.TotalMilliseconds;
        bool filtered = thresholds.ContainsKey(vk);

        if (!isDown)
        {
            if (s.PressAccepted) s.LastAcceptedUp = now;
            return;
        }

        if (!filtered)
        {
            s.Presses++;
            s.PressAccepted = true;
            s.LastAcceptedDown = now;
            return;
        }

        double reference = measureFrom.Equals("Release", StringComparison.OrdinalIgnoreCase)
            ? s.LastAcceptedUp : s.LastAcceptedDown;
        double gap = reference < 0 ? double.MaxValue : now - reference;

        if (gap < thresholds[vk])
        {
            s.Blocked++;
            s.PressAccepted = false;          // its release must not reset the clock
            s.LastBlockedTime = DateTime.Now.ToString("HH:mm:ss");
            if (Line != null)
            {
                // The launcher logs for the whole session now, so a hard cap would
                // silently stop recording once a key has bounced a lot. Log the
                // first LogLineCap per key in full, then at most one line per
                // second per key, so a busy key cannot flood the file.
                if (s.Blocked <= LogLineCap || now - s.LastLoggedMs >= 1000)
                {
                    s.LastLoggedMs = now;
                    Line(KeyNames.Pretty(KeyNames.FromVk(vk)) + L.T("  拦下一次抖动：距上次 ") +
                         gap.ToString("0.0") + L.T("ms  < 阈值 ") + thresholds[vk] + "ms");
                }
            }
        }
        else
        {
            s.Presses++;
            s.PressAccepted = true;
            s.LastAcceptedDown = now;
        }
    }
}

// --------------------------------------------------------------- prompts ----

static class Prompts
{
    public static string Ask(string text, string initial)
    {
        Form f = new Form();
        f.Text = "KeyDebounce";
        f.ClientSize = new Size(430, 155);
        f.StartPosition = FormStartPosition.CenterScreen;
        f.FormBorderStyle = FormBorderStyle.FixedDialog;
        f.MaximizeBox = false; f.MinimizeBox = false;
        f.Font = new Font("Microsoft YaHei UI", 9f);

        Label l = new Label(); l.Text = text; l.SetBounds(12, 10, 406, 62);
        TextBox t = new TextBox(); t.Text = initial; t.SetBounds(12, 76, 130, 24);
        Button ok = new Button(); ok.Text = L.T("确定"); ok.DialogResult = DialogResult.OK; ok.SetBounds(228, 112, 84, 26);
        Button no = new Button(); no.Text = L.T("取消"); no.DialogResult = DialogResult.Cancel; no.SetBounds(322, 112, 84, 26);

        f.Controls.Add(l); f.Controls.Add(t); f.Controls.Add(ok); f.Controls.Add(no);
        f.AcceptButton = ok; f.CancelButton = no;
        return f.ShowDialog() == DialogResult.OK ? t.Text : null;
    }
}

// ------------------------------------------------------------ key manager ---

class ManagerForm : Form
{
    ListView list;
    List<KeyValuePair<string, string>> keys = new List<KeyValuePair<string, string>>();

    public ManagerForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        TopLevel = false;
        Dock = DockStyle.Fill;
        Font = new Font("Microsoft YaHei UI", 9f);

        Label hint = new Label();
        hint.Text = L.T("阈值单位毫秒，只作用于列在这里的键；其它键完全不受影响（global_chatter = 0）。\r\n") +
                    L.T("抖动反弹通常 5–30ms，正常连按通常 ≥95ms —— 取两者中间的值。");
        hint.SetBounds(12, 12, 720, 40);

        list = new ListView();
        list.View = View.Details;
        list.FullRowSelect = true;
        list.MultiSelect = true;
        list.GridLines = true;
        list.SetBounds(12, 58, 720, 296);
        list.Columns.Add(L.T("按键"), 150);
        list.Columns.Add(L.T("阈值 (ms)"), 90);
        list.Columns.Add(L.T("配置项"), 220);
        list.Columns.Add(L.T("来源"), 120);

        Button add = new Button();
        add.Text = L.T("添加按键…");
        add.SetBounds(12, 366, 110, 30);
        add.Click += delegate { AddKeys(); };

        Button edit = new Button();
        edit.Text = L.T("改阈值…");
        edit.SetBounds(130, 366, 90, 30);
        edit.Click += delegate { EditThreshold(); };

        Button del = new Button();
        del.Text = L.T("删除选中");
        del.SetBounds(228, 366, 90, 30);
        del.Click += delegate { RemoveSelected(); };

        Button reload = new Button();
        reload.Text = L.T("重新载入");
        reload.SetBounds(326, 366, 90, 30);
        reload.Click += delegate { Reload(); };

        Button save = new Button();
        save.Text = L.T("保存并重启引擎");
        save.SetBounds(580, 366, 152, 30);
        save.Click += delegate { SaveAndApply(); };

        Label foot = new Label();
        foot.Text = L.T("提示：不要过滤 Shift / Ctrl / Alt —— 会影响所有快捷键；鼠标抖动可用「添加按键」里的下拉框。");
        foot.ForeColor = Color.DimGray;
        foot.SetBounds(12, 404, 720, 20);

        Controls.Add(hint); Controls.Add(list); Controls.Add(add);
        Controls.Add(edit); Controls.Add(del); Controls.Add(reload);
        Controls.Add(save); Controls.Add(foot);

        Reload();
    }

    /// Re-reads config.txt and re-renders. Local edits use Render() only, so
    /// unsaved changes are never silently thrown away.
    public void Reload()
    {
        keys = Cfg.LoadKeys();
        Render();
    }

    void Render()
    {
        list.Items.Clear();
        foreach (KeyValuePair<string, string> kv in keys)
        {
            ListViewItem item = new ListViewItem(KeyNames.Pretty(kv.Key));
            item.SubItems.Add(kv.Value);
            item.SubItems.Add("key." + kv.Key);
            item.SubItems.Add(KeyNames.IsMouse(kv.Key) ? L.T("鼠标") : L.T("键盘"));
            item.Tag = kv.Key;
            list.Items.Add(item);
        }
    }

    void AddKeys()
    {
        using (CaptureForm cap = new CaptureForm())
        {
            cap.ShowDialog();
            if (cap.Captured.Count == 0) return;

            int added = 0;
            foreach (string name in cap.Captured)
            {
                bool dup = false;
                foreach (KeyValuePair<string, string> kv in keys)
                    if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)) dup = true;
                if (dup) continue;

                if (name == "LShiftKey" || name == "RShiftKey" || name == "ShiftKey" ||
                    name == "LControlKey" || name == "RControlKey" || name == "ControlKey" ||
                    name == "LMenu" || name == "RMenu" || name == "Menu")
                {
                    if (MessageBox.Show(L.T("「") + KeyNames.Pretty(name) + L.T("」是修饰键，过滤它会让 Ctrl+C 这类") +
                                        L.T("组合键在快速按动时失效。仍然要加吗？"), L.T("确认"),
                                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                        continue;
                }

                keys.Add(new KeyValuePair<string, string>(name, "50"));
                added++;
            }
            Render();
            if (added > 0)
                MessageBox.Show(L.T("已加入 ") + added + L.T(" 个按键（阈值默认 50ms）。\r\n改好阈值后点「保存并重启引擎」。"),
                                "KeyDebounce");
        }
    }

    void EditThreshold()
    {
        if (list.SelectedItems.Count == 0) { MessageBox.Show(L.T("先选一行。")); return; }
        string name = (string)list.SelectedItems[0].Tag;
        string cur = "50";
        foreach (KeyValuePair<string, string> kv in keys) if (kv.Key == name) cur = kv.Value;

        string input = Prompts.Ask(L.T("把「") + KeyNames.Pretty(name) + L.T("」的阈值改成多少毫秒？") +
                                   L.T("\r\n建议：明显大于抖动值、明显小于你最快的连续两次按键间隔。"), cur);
        if (input == null) return;
        int ms;
        if (!int.TryParse(input.Trim(), out ms) || ms < 1 || ms > 1000)
        {
            MessageBox.Show(L.T("请输入 1–1000 之间的整数。")); return;
        }
        for (int i = 0; i < keys.Count; i++)
            if (keys[i].Key == name) keys[i] = new KeyValuePair<string, string>(name, ms.ToString());
        Render();
    }

    void RemoveSelected()
    {
        if (list.SelectedItems.Count == 0) { MessageBox.Show(L.T("先选要删的行（可多选）。")); return; }
        List<string> gone = new List<string>();
        foreach (ListViewItem i in list.SelectedItems) gone.Add((string)i.Tag);
        keys.RemoveAll(delegate(KeyValuePair<string, string> kv) { return gone.Contains(kv.Key); });
        Render();
    }

    void SaveAndApply()
    {
        if (keys.Count == 0 &&
            MessageBox.Show(L.T("现在没有任何过滤按键，等于关闭过滤。确定保存吗？"), "KeyDebounce",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;
        try
        {
            Cfg.SaveKeys(keys);
            Cfg.AppendLog(L.T("按键配置已更新（") + keys.Count + L.T(" 个过滤按键）"));
            MessageBox.Show(L.T("已保存。\r\n") + Cfg.RestartEngine(), "KeyDebounce");
            Reload();
        }
        catch (Exception ex) { MessageBox.Show(L.T("保存失败：") + ex.Message); }
    }
}

// ------------------------------------------------------------ key capture ---

class CaptureForm : Form
{
    public List<string> Captured = new List<string>();

    KeyboardObserver obs;
    ListBox shown;

    public CaptureForm()
    {
        Text = L.T("按一下要过滤的键");
        ClientSize = new Size(450, 330);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9f);

        Label l = new Label();
        l.Text = L.T("请按一下想过滤的键（可连续按多个，全部会被加入）。\r\n") +
                 L.T("这里按下的键只被记录，不会被输入到其它程序。Esc 结束后点「完成」。");
        l.SetBounds(12, 10, 426, 44);

        shown = new ListBox();
        shown.SetBounds(12, 58, 426, 170);

        ComboBox mouse = new ComboBox();
        mouse.DropDownStyle = ComboBoxStyle.DropDownList;
        foreach (string m in KeyNames.MouseChoices) mouse.Items.Add(KeyNames.Pretty(m));
        mouse.SetBounds(12, 240, 170, 24);

        Button addMouse = new Button();
        addMouse.Text = L.T("加入鼠标键");
        addMouse.SetBounds(190, 239, 110, 26);
        addMouse.Click += delegate
        {
            if (mouse.SelectedIndex < 0) { MessageBox.Show(L.T("先选一个鼠标目标。")); return; }
            string name = KeyNames.MouseChoices[mouse.SelectedIndex];
            if (!Captured.Contains(name))
            {
                Captured.Add(name);
                shown.Items.Add(KeyNames.Pretty(name) + "   ->  key." + name);
            }
        };

        Button done = new Button();
        done.Text = L.T("完成");
        done.DialogResult = DialogResult.OK;
        done.SetBounds(330, 239, 108, 26);

        Label foot = new Label();
        foot.Text = L.T("提示：按 Esc 会结束采集，Esc 本身不会被加入过滤。");
        foot.ForeColor = Color.DimGray;
        foot.SetBounds(12, 272, 426, 20);

        Controls.Add(l); Controls.Add(shown); Controls.Add(mouse);
        Controls.Add(addMouse); Controls.Add(done); Controls.Add(foot);

        obs = new KeyboardObserver();
        obs.Key += OnKey;
        FormClosed += delegate { obs.Dispose(); };
    }

    void OnKey(int vk, bool isDown, bool injected)
    {
        if (!isDown || injected) return;
        if (vk == 0x1B) { DialogResult = DialogResult.OK; Close(); return; }
        string name = KeyNames.FromVk(vk);
        if (Captured.Contains(name)) return;
        Captured.Add(name);
        shown.Items.Add(KeyNames.Pretty(name) + "   ->  key." + name);
    }
}

// --------------------------------------------------------------- scanner ----

class ScannerForm : Form
{
    class Stat
    {
        public List<double> Marks = new List<double>();
        public int GapsUnder20, Gaps20To50, InjectedDowns;
        public double MinGap = double.MaxValue;
    }

    KeyboardObserver obs;
    Timer refresh;
    Stopwatch clock = new Stopwatch();
    Dictionary<int, Stat> stats = new Dictionary<int, Stat>();
    ListView list;
    Label foot;
    Button startStop;
    bool running;

    public ScannerForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        TopLevel = false;
        Dock = DockStyle.Fill;
        Font = new Font("Microsoft YaHei UI", 9f);

        Label l = new Label();
        l.Text = L.T("点「开始扫描」，然后像平时一样打字，并特意多按几次你怀疑的按键（每次一下，约 20–30 次）。\r\n") +
                 L.T("扫描看的是键盘原始信号（在过滤之前），所以能发现还没被加进过滤的抖动键。");
        l.SetBounds(12, 12, 720, 44);

        list = new ListView();
        list.View = View.Details;
        list.FullRowSelect = true;
        list.GridLines = true;
        list.SetBounds(12, 62, 720, 276);
        list.Columns.Add(L.T("按键"), 110);
        list.Columns.Add(L.T("按下次数"), 80);
        list.Columns.Add("<20ms", 70);
        list.Columns.Add("20–50ms", 80);
        list.Columns.Add(L.T("最短间隔"), 110);
        list.Columns.Add(L.T("判定"), 260);

        foot = new Label();
        foot.ForeColor = Color.DimGray;
        foot.SetBounds(12, 346, 720, 40);
        foot.Text = L.T("判定规则：同一个键两次按下间隔 < 40ms，物理上不可能，出现即判为抖动。");

        startStop = new Button();
        startStop.Text = L.T("开始扫描");
        startStop.SetBounds(12, 388, 120, 30);
        startStop.Click += delegate { Toggle(); };

        Button adopt = new Button();
        adopt.Text = L.T("把疑似抖动的键加入过滤");
        adopt.SetBounds(140, 388, 220, 30);
        adopt.Click += delegate { Adopt(); };

        Button clear = new Button();
        clear.Text = L.T("清空结果");
        clear.SetBounds(368, 388, 100, 30);
        clear.Click += delegate { stats.Clear(); list.Items.Clear(); };

        Controls.Add(l); Controls.Add(list); Controls.Add(foot);
        Controls.Add(startStop); Controls.Add(adopt); Controls.Add(clear);

        obs = new KeyboardObserver();
        obs.Key += OnKey;

        refresh = new Timer();
        refresh.Interval = 400;
        refresh.Tick += delegate { Rebuild(); };
        refresh.Start();

        FormClosed += delegate { obs.Dispose(); refresh.Stop(); };
    }

    void Toggle()
    {
        running = !running;
        if (running)
        {
            stats.Clear();
            list.Items.Clear();
            clock.Reset();
            clock.Start();
            startStop.Text = L.T("停止扫描");
            foot.Text = L.T("扫描中…现在请打字，并多按几次怀疑的按键。");
        }
        else
        {
            startStop.Text = L.T("开始扫描");
            Rebuild();
        }
    }

    void OnKey(int vk, bool isDown, bool injected)
    {
        if (!running) return;
        Stat s;
        if (!stats.TryGetValue(vk, out s)) { s = new Stat(); stats[vk] = s; }

        if (!isDown) { s.Marks.Add(-1); return; }
        if (injected) s.InjectedDowns++;

        double? prev = null;
        for (int i = s.Marks.Count - 1; i >= 0; i--)
            if (s.Marks[i] >= 0) { prev = s.Marks[i]; break; }

        double now = clock.Elapsed.TotalMilliseconds;
        if (prev.HasValue)
        {
            double gap = now - prev.Value;
            if (gap < s.MinGap) s.MinGap = gap;
            if (gap < 20) s.GapsUnder20++;
            else if (gap < 50) s.Gaps20To50++;
        }
        s.Marks.Add(now);
        if (s.Marks.Count > 4000) s.Marks.RemoveAt(0);
    }

    bool Chattery(Stat s)
    {
        int presses = 0;
        foreach (double d in s.Marks) if (d >= 0) presses++;
        return presses >= 3 && s.MinGap < 40;
    }

    void Rebuild()
    {
        list.BeginUpdate();
        list.Items.Clear();
        int inj = 0;
        foreach (KeyValuePair<int, Stat> kv in stats)
        {
            Stat s = kv.Value;
            int presses = 0;
            foreach (double d in s.Marks) if (d >= 0) presses++;
            if (presses == 0) continue;
            inj += s.InjectedDowns;

            ListViewItem item = new ListViewItem(KeyNames.Pretty(KeyNames.FromVk(kv.Key)));
            item.SubItems.Add(presses.ToString());
            item.SubItems.Add(s.GapsUnder20.ToString());
            item.SubItems.Add(s.Gaps20To50.ToString());
            item.SubItems.Add(s.MinGap == double.MaxValue ? "-" : s.MinGap.ToString("0.0") + " ms");
            item.SubItems.Add(Chattery(s) ? L.T("疑似抖动 — 建议过滤") : L.T("正常"));
            if (Chattery(s)) item.ForeColor = Color.Firebrick;
            item.Tag = kv.Key;
            list.Items.Add(item);
        }
        list.EndUpdate();

        if (inj > 0)
            foot.Text = L.T("（本次含 ") + inj + L.T(" 次软件注入的按键，只有测试才会出现；真实手按不会有。）");
    }

    void Adopt()
    {
        List<KeyValuePair<string, string>> keys = Cfg.LoadKeys();
        List<string> names = new List<string>();
        foreach (KeyValuePair<int, Stat> kv in stats)
        {
            if (!Chattery(kv.Value)) continue;
            string name = KeyNames.FromVk(kv.Key);
            bool dup = false;
            foreach (KeyValuePair<string, string> k in keys)
                if (string.Equals(k.Key, name, StringComparison.OrdinalIgnoreCase)) dup = true;
            if (!dup) { keys.Add(new KeyValuePair<string, string>(name, "50")); names.Add(name); }
        }
        if (names.Count == 0) { MessageBox.Show(L.T("没有发现需要新加的抖动按键。")); return; }
        if (MessageBox.Show(L.T("将加入（阈值 50ms）：") + string.Join(L.T("、"), names.ToArray()) +
                            L.T("\r\n\r\n保存并重启引擎？"), "KeyDebounce",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            Cfg.SaveKeys(keys);
            Cfg.AppendLog(L.T("扫描加入过滤按键：") + string.Join(L.T("、"), names.ToArray()));
            MessageBox.Show(L.T("已保存。\r\n") + Cfg.RestartEngine(), "KeyDebounce");
        }
        catch (Exception ex) { MessageBox.Show(L.T("保存失败：") + ex.Message); }
    }
}

// --------------------------------------------------------------- main UI ----

class MainForm : Form
{
    TabControl tabs;
    Label lblState, lblDetail, statNote;
    ListView statList;
    TextBox logBox, cfgBox;
    CheckBox autoScroll;
    Timer tick;
    ManagerForm mgr;
    ScannerForm scan;
    long lastLogLen = -1;
    int tickCount;

    public MainForm(string startTab, bool includeInjected)
    {
        Text = L.T("KeyDebounce — 主界面") + "   v" + AppInfo.Version;
        ClientSize = new Size(772, 512);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9f);
        MinimumSize = new Size(720, 470);

        tabs = new TabControl();
        tabs.Dock = DockStyle.Fill;

        TabPage pOverview = new TabPage(L.T("概览"));
        TabPage pKeys = new TabPage(L.T("过滤按键"));
        TabPage pScan = new TabPage(L.T("抖动扫描"));
        TabPage pLog = new TabPage(L.T("日志"));
        TabPage pCfg = new TabPage(L.T("配置"));
        tabs.TabPages.Add(pOverview);
        tabs.TabPages.Add(pKeys);
        tabs.TabPages.Add(pScan);
        tabs.TabPages.Add(pLog);
        tabs.TabPages.Add(pCfg);

        BuildOverview(pOverview);
        BuildLog(pLog);
        BuildConfig(pCfg);

        mgr = new ManagerForm();
        pKeys.Controls.Add(mgr);
        mgr.Show();

        scan = new ScannerForm();
        pScan.Controls.Add(scan);
        scan.Show();

        Controls.Add(tabs);
        if (startTab == "keys") tabs.SelectedIndex = 1;
        else if (startTab == "scan") tabs.SelectedIndex = 2;
        else if (startTab == "log") tabs.SelectedIndex = 3;
        else if (startTab == "config") tabs.SelectedIndex = 4;

        // The intercept mirror lives in the tray launcher, not here: it must keep
        // recording for the whole session, and its counters have to survive
        // closing and reopening this window. They reach us through
        // KeyDebounce.stats. includeInjected is kept for CLI compatibility and
        // applies to the launcher (KeyDebounce.exe --allow-injected).

        tick = new Timer();
        tick.Interval = 1000;
        tick.Tick += delegate { OnTick(); };
        tick.Start();

        LoadConfigIntoBox();
        OnTick();
        FormClosed += delegate { tick.Stop(); };
    }

    void BuildOverview(TabPage p)
    {
        lblState = new Label();
        lblState.SetBounds(16, 14, 730, 32);
        lblState.Font = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold);

        lblDetail = new Label();
        lblDetail.SetBounds(18, 48, 730, 66);

        Button bStart = new Button();
        bStart.Text = L.T("启动过滤");
        bStart.SetBounds(16, 118, 100, 30);
        bStart.Click += delegate { Act(Cfg.StartEngine()); };

        Button bStop = new Button();
        bStop.Text = L.T("停止过滤");
        bStop.SetBounds(124, 118, 100, 30);
        bStop.Click += delegate
        {
            if (MessageBox.Show(L.T("停止后键盘抖动会重新出现（直到再次启动）。确定停止吗？"), "KeyDebounce",
                                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            Act(Cfg.StopEngine());
        };

        Button bRestart = new Button();
        bRestart.Text = L.T("重启引擎");
        bRestart.SetBounds(232, 118, 100, 30);
        bRestart.Click += delegate { Act(Cfg.RestartEngine()); };

        Button bOpenCfg = new Button();
        bOpenCfg.Text = L.T("用记事本打开配置");
        bOpenCfg.SetBounds(340, 118, 150, 30);
        bOpenCfg.Click += delegate { Process.Start("notepad.exe", "\"" + Cfg.ConfigPath + "\""); };

        Button bOpenDir = new Button();
        bOpenDir.Text = L.T("打开程序文件夹");
        bOpenDir.SetBounds(498, 118, 150, 30);
        bOpenDir.Click += delegate { Process.Start(Cfg.Dir); };

        Label statTitle = new Label();
        statTitle.Text = L.T("实时拦截镜像（与引擎同规则，在过滤前取样）");
        statTitle.SetBounds(16, 160, 500, 20);

        statList = new ListView();
        statList.View = View.Details;
        statList.FullRowSelect = true;
        statList.GridLines = true;
        statList.SetBounds(16, 184, 730, 226);
        statList.Columns.Add(L.T("按键"), 120);
        statList.Columns.Add(L.T("阈值"), 100);
        statList.Columns.Add(L.T("放行的按下"), 120);
        statList.Columns.Add(L.T("拦下的抖动"), 120);
        statList.Columns.Add(L.T("最近一次拦截"), 150);

        statNote = new Label();
        statNote.ForeColor = Color.DimGray;
        statNote.SetBounds(16, 416, 730, 44);

        p.Controls.Add(lblState); p.Controls.Add(lblDetail);
        p.Controls.Add(bStart); p.Controls.Add(bStop); p.Controls.Add(bRestart);
        p.Controls.Add(bOpenCfg); p.Controls.Add(bOpenDir);
        p.Controls.Add(statTitle); p.Controls.Add(statList); p.Controls.Add(statNote);
    }

    void Act(string msg)
    {
        Cfg.AppendLog(msg);
        OnTick();
    }

    void BuildLog(TabPage p)
    {
        logBox = new TextBox();
        logBox.Multiline = true;
        logBox.ReadOnly = true;
        logBox.ScrollBars = ScrollBars.Both;
        logBox.WordWrap = false;
        logBox.Font = new Font("Consolas", 9f);
        logBox.SetBounds(12, 12, 736, 396);

        autoScroll = new CheckBox();
        autoScroll.Text = L.T("自动滚动到最新");
        autoScroll.Checked = true;
        autoScroll.SetBounds(12, 416, 160, 24);

        Button reload = new Button();
        reload.Text = L.T("重新载入");
        reload.SetBounds(190, 414, 90, 26);
        reload.Click += delegate { lastLogLen = -1; RefreshLog(); };

        Button clearView = new Button();
        clearView.Text = L.T("清空显示");
        clearView.SetBounds(288, 414, 90, 26);
        clearView.Click += delegate { logBox.Text = ""; };

        Button openLog = new Button();
        openLog.Text = L.T("打开日志文件");
        openLog.SetBounds(386, 414, 120, 26);
        openLog.Click += delegate { Process.Start("notepad.exe", "\"" + Cfg.LogPath + "\""); };

        Label note = new Label();
        note.ForeColor = Color.DimGray;
        note.Text = L.T("记录：启停与配置变更、以及每一次被拦下的抖动（含间隔毫秒）。");
        note.SetBounds(516, 418, 240, 40);

        p.Controls.Add(logBox); p.Controls.Add(autoScroll); p.Controls.Add(reload);
        p.Controls.Add(clearView); p.Controls.Add(openLog); p.Controls.Add(note);
    }

    void BuildConfig(TabPage p)
    {
        Label l = new Label();
        l.Text = L.T("高级：直接编辑 config.txt。保存前会校验 key. 名称（写错会让引擎启动时弹错误框）。");
        l.SetBounds(12, 12, 736, 20);

        cfgBox = new TextBox();
        cfgBox.Multiline = true;
        cfgBox.ScrollBars = ScrollBars.Both;
        cfgBox.WordWrap = false;
        cfgBox.Font = new Font("Consolas", 9f);
        cfgBox.SetBounds(12, 36, 736, 364);

        Button reload = new Button();
        reload.Text = L.T("重新载入");
        reload.SetBounds(12, 410, 100, 28);
        reload.Click += delegate { LoadConfigIntoBox(); };

        Button validate = new Button();
        validate.Text = L.T("校验");
        validate.SetBounds(120, 410, 80, 28);
        validate.Click += delegate { MessageBox.Show(ValidateConfigText(cfgBox.Text)); };

        Button save = new Button();
        save.Text = L.T("保存并重启引擎");
        save.SetBounds(208, 410, 160, 28);
        save.Click += delegate { SaveConfigText(); };

        p.Controls.Add(l); p.Controls.Add(cfgBox);
        p.Controls.Add(reload); p.Controls.Add(validate); p.Controls.Add(save);
    }

    void LoadConfigIntoBox()
    {
        try
        {
            cfgBox.Text = File.Exists(Cfg.ConfigPath)
                ? File.ReadAllText(Cfg.ConfigPath).Replace("\r\n", "\n")
                : "";
        }
        catch (Exception ex) { cfgBox.Text = L.T("(读取失败：") + ex.Message + ")"; }
    }

    void SaveConfigText()
    {
        string verdict = ValidateConfigText(cfgBox.Text);
        if (verdict != L.T("配置看起来没问题。"))
        {
            MessageBox.Show(verdict, L.T("无法保存"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            File.WriteAllText(Cfg.ConfigPath, cfgBox.Text.Replace("\n", "\r\n"), new UTF8Encoding(false));
            Cfg.AppendLog(L.T("配置文件被直接编辑并保存"));
            string msg = Cfg.RestartEngine();
            mgr.Reload();
            MessageBox.Show(L.T("已保存。\r\n") + msg, "KeyDebounce");
            LoadConfigIntoBox();
        }
        catch (Exception ex) { MessageBox.Show(L.T("保存失败：") + ex.Message); }
    }

    static string ValidateConfigText(string text)
    {
        List<string> bad = new List<string>();
        foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            string t = raw.Trim();
            if (t.Length == 0 || t[0] == '#') continue;
            int c = t.IndexOf(':');
            if (c <= 0) { bad.Add(L.T("缺少冒号：") + t); continue; }
            string name = t.Substring(0, c).Trim();
            string val = t.Substring(c + 1).Trim();
            if (!name.StartsWith("key.", StringComparison.OrdinalIgnoreCase)) continue;
            string key = name.Substring(4);
            int vk, ms;
            if (!KeyNames.IsMouse(key) && !KeyNames.TryVk(key, out vk))
                bad.Add(L.T("按键名无效：") + name);
            else if (!int.TryParse(val, out ms) || ms < 0 || ms > 1000)
                bad.Add(L.T("阈值不是 0–1000 的整数：") + t);
        }
        if (bad.Count == 0) return L.T("配置看起来没问题。");
        return L.T("发现 ") + bad.Count + L.T(" 处问题，保存被拒绝：\r\n\r\n  ") + string.Join("\r\n  ", bad.ToArray()) +
               L.T("\r\n\r\n（引擎遇到无效按键名会在启动时弹错误框，所以这里先拦住。）");
    }

    void OnTick()
    {
        bool running = Cfg.EngineRunning();
        int filtered = Cfg.FilteredKeyCount();
        lblState.ForeColor = running ? Color.FromArgb(30, 130, 50) : Color.FromArgb(170, 50, 50);
        lblState.Text = running ? L.T("● 正在过滤") : L.T("● 未在过滤");

        string mf = Cfg.ReadSetting("measure_from");
        lblDetail.Text =
            L.T("引擎：KeyboardChatterBlocker") + (running ? L.T("（PID ") + Cfg.EnginePid() + L.T("）") : L.T("（未运行）")) +
            L.T("    过滤按键：") + filtered + L.T(" 个    计时基准：") + (mf == "" ? "Press" : mf) +
            L.T("\r\n配置：") + Cfg.ConfigPath +
            L.T("\r\n开机自启：") + (File.Exists(Cfg.StartupLnk) ? L.T("已开启") : L.T("未开启")) +
            L.T("    托盘图标：") + (running ? L.T("绿色") : L.T("红色"));

        tickCount++;
        if (tickCount % 2 == 0) RefreshStats();
        RefreshLog();
    }

    void RefreshStats()
    {
        statList.BeginUpdate();
        statList.Items.Clear();

        // Counters come from the tray launcher (KeyDebounce.stats), which runs
        // from boot to shutdown -- so they keep accumulating while this window is
        // closed, instead of resetting on every reopen.
        try
        {
            if (File.Exists(Cfg.MirrorStatsPath))
            {
                foreach (string raw in File.ReadAllLines(Cfg.MirrorStatsPath))
                {
                    string line = raw.Trim();
                    if (line.Length == 0) continue;
                    if (line[0] == '#') continue;
                    string[] f = line.Split(',');
                    if (f.Length < 5) continue;
                    ListViewItem item = new ListViewItem(KeyNames.Pretty(f[0]));
                    item.SubItems.Add(f[1] == "0" ? L.T("不过滤") : f[1] + " ms");
                    item.SubItems.Add(f[2]);
                    item.SubItems.Add(f[3]);
                    item.SubItems.Add(f[4]);
                    statList.Items.Add(item);
                }
            }
        }
        catch { }
        statList.EndUpdate();

        string statsInfo = L.T("尚未生成");
        try
        {
            if (File.Exists(Cfg.StatsPath))
            {
                FileInfo fi = new FileInfo(Cfg.StatsPath);
                statsInfo = fi.Length == 0
                    ? L.T("已生成但为空（引擎退出时才写入）")
                    : fi.LastWriteTime.ToString("HH:mm:ss") + L.T(" 更新");
            }
        }
        catch { }
        statNote.Text = L.T("「拦下的抖动」由本程序按引擎规则实时镜像得出；引擎自身只在退出或改配置时写 ") +
                        L.T("blocker_stats.csv（当前：") + statsInfo + L.T("）。");
    }

    void RefreshLog()
    {
        try
        {
            if (!File.Exists(Cfg.LogPath)) return;
            long len = new FileInfo(Cfg.LogPath).Length;
            if (len == lastLogLen) return;
            lastLogLen = len;

            // Read only the tail: the log now runs from boot to shutdown, so
            // slurping the whole file every second would get slower the longer
            // the session lives.
            const int windowBytes = 64 * 1024;
            long start = len > windowBytes ? len - windowBytes : 0;
            byte[] buf = new byte[len - start];
            using (FileStream fs = new FileStream(Cfg.LogPath, FileMode.Open,
                                                  FileAccess.Read, FileShare.ReadWrite))
            {
                fs.Seek(start, SeekOrigin.Begin);
                int got = 0;
                while (got < buf.Length)
                {
                    int n = fs.Read(buf, got, buf.Length - got);
                    if (n <= 0) break;
                    got += n;
                }
            }
            string all = Encoding.UTF8.GetString(buf, 0, buf.Length).Replace("\r\n", "\n");
            if (start > 0)
            {
                int nl = all.IndexOf('\n');       // drop the partially-read first line
                if (nl >= 0) all = all.Substring(nl + 1);
            }
            string[] lines = all.Split('\n');
            int take = Math.Min(lines.Length, 400);
            StringBuilder tail = new StringBuilder();
            for (int i = lines.Length - take; i < lines.Length; i++)
                if (i >= 0 && lines[i].Length > 0) tail.Append(lines[i]).Append("\r\n");
            logBox.Text = tail.ToString();

            if (autoScroll.Checked && logBox.Text.Length > 0)
            {
                logBox.SelectionStart = logBox.Text.Length;
                logBox.ScrollToCaret();
            }
        }
        catch { }
    }
}
