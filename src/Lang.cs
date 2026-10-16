// Lang.cs -- tiny bilingual layer for KeyDebounce.
//
// The UI strings in the sources stay written in Chinese; every user-visible
// literal is wrapped in L.T("…") by tools/wrap_i18n.py, and L.T translates it to
// English when the UI language is English. The Chinese literal is the lookup
// key, so a missing translation degrades to Chinese rather than to a blank or a
// crash -- and every miss is recorded in L.Missing so it can be logged.
//
// Language selection: automatic from CultureInfo.CurrentUICulture ("zh*" ->
// Chinese, anything else -> English), overridable with --zh / --en on the
// command line (handy for screenshots and support).
//
// C# 5 only (in-box csc.exe).

using System;
using System.Collections.Generic;
using System.Globalization;

static class L
{
    public static bool En = AutoDetect();
    public static List<string> Missing = new List<string>();

    static bool AutoDetect()
    {
        try
        {
            string two = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return !two.Equals("zh", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// Command line override: --en / --zh anywhere in args.
    public static void SetFromArgs(string[] args)
    {
        if (args == null) return;
        foreach (string a in args)
        {
            string f = a.ToLowerInvariant();
            if (f == "--en") En = true;
            if (f == "--zh") En = false;
        }
    }

    public static string T(string zh)
    {
        if (!En || zh == null) return zh;
        string en;
        if (Map.TryGetValue(zh, out en)) return en;
        if (!Missing.Contains(zh)) Missing.Add(zh);
        return zh;
    }

    static readonly Dictionary<string, string> Map = new Dictionary<string, string>
    {
        // --- tray launcher -------------------------------------------------
        { "KeyDebounce已经在运行了。\r\n请查看屏幕右下角托盘里的图标（绿色 = 正在过滤）。",
          "KeyDebounce is already running.\r\nLook for the tray icon at the bottom-right of your screen (green = filtering)." },
        { "。\r\n请把它和本程序放在同一个文件夹里。",
          ".\r\nPut it in the same folder as this program." },
        { "=== KeyDebounce 启动，目录 ", "=== KeyDebounce started, folder " },
        { "=== KeyDebounce 退出 ===", "=== KeyDebounce exiting ===" },
        { "已生成 config.txt（E 键 50ms / measure_from: Release）",
          "created config.txt (per key 50 ms, measure_from: Release)" },
        { "config.txt: is_enabled 原本是关的，已改为 true",
          "config.txt: is_enabled was off, set to true" },
        { "config.txt: 缺少 key.E 条目，已补上 50ms",
          "config.txt: no key.E entry found, added one at 50 ms" },
        { "读取 config.txt 失败: ", "failed to read config.txt: " },
        { "引擎已在运行，不重复启动（重复启动会装第二个键盘钩子）",
          "engine already running, not starting a second one (a second engine = a second keyboard hook)" },
        { "已启动引擎，pid=", "engine started, pid=" },
        { "KeyDebounce已启动", "KeyDebounce started" },
        { "引擎正在运行：只过滤 E 键（50ms），其它按键不受影响。",
          "The engine is running: only the keys in your filter list are affected." },
        { "启动引擎失败: ", "failed to start the engine: " },
        { "启动引擎失败：", "Failed to start the engine: " },
        { "已停止引擎，pid=", "engine stopped, pid=" },
        { "停止引擎失败: ", "failed to stop the engine: " },
        { "状态：检查中…", "Status: checking…" },
        { "开机自启", "Start with Windows" },
        { "已开启开机自启", "autostart enabled" },
        { "已关闭开机自启", "autostart disabled" },
        { "设置开机自启失败: ", "failed to set autostart: " },
        { "设置开机自启失败：", "Failed to set autostart: " },
        { "打开配置文件（改阈值）", "Open config.txt (thresholds)" },
        { "打开使用说明", "Open documentation" },
        { "打开安装文件夹", "Open program folder" },
        { "打开主界面（配置 / 日志 / 扫描）", "Main window (config / log / scan)" },
        { "查看实时日志", "Live log" },
        { "关于 / 当前状态", "About / current status" },
        { "退出", "Quit" },
        { "同时停止键盘过滤引擎吗？\r\n\r\n是 = 关掉过滤，退出本程序（键盘抖动会重新出现）\r\n否 = 保留引擎继续过滤，只退出托盘程序",
          "Stop the filter engine as well?\r\n\r\nYes = turn filtering off and quit (key chatter comes back)\r\nNo = leave the engine filtering and quit only this tray app" },
        { "退出 KeyDebounce", "Quit KeyDebounce" },
        { "KeyDebounce：正在检查…", "KeyDebounce: checking…" },
        { "已启动。绿色图标 = 正在过滤 E 键抖动。",
          "Started. A green icon means chatter is being filtered." },
        { "检测到引擎在运行", "engine is running" },
        { "检测到引擎已停止", "engine has stopped" },
        { "过滤已停止", "Filtering stopped" },
        { "KeyboardChatterBlocker 不再运行，正在尝试恢复。",
          "KeyboardChatterBlocker is no longer running; trying to bring it back." },
        { "引擎未运行且未手动暂停 -> 自动重启（第 ", "engine not running and not paused -> auto-restart (attempt " },
        { " 次）", ")" },
        { "KeyDebounce：正在过滤（阈值 ", "KeyDebounce: filtering (threshold " },
        { "ms，仅 E 键）", " ms)" },
        { "KeyDebounce：未过滤（引擎没在运行）", "KeyDebounce: not filtering (engine is not running)" },
        { "状态：正在过滤（阈值 ", "Status: filtering (threshold " },
        { "ms）", " ms)" },
        { "状态：未过滤（引擎未运行）", "Status: not filtering (engine not running)" },
        { "开始过滤（启动引擎）", "Start filtering (start engine)" },
        { "正在过滤", "filtering" },
        { "未在过滤", "not filtering" },
        { "引擎：KeyboardChatterBlocker（E 键 ", "Engine: KeyboardChatterBlocker (threshold " },
        { "ms，", " ms, " },
        { "measure_from: Release）\r\n", "measure_from: Release)\r\n" },
        { "配置：", "Config: " },
        { "开机自启：", "Autostart: " },
        { "托盘图标：绿色 = 过滤中，红色 = 已停止\r\n\r\n",
          "Tray icon: green = filtering, red = stopped\r\n\r\n" },
        { "内核驱动、注册表、服务：都没有改动。删除本文件夹即可彻底移除。",
          "No kernel driver, registry value or service is touched. Delete this folder to remove it completely." },
        { "打开失败 ", "failed to open " },
        { "打开主界面失败: ", "failed to open the main window: " },
        { "暂停过滤（停止引擎）", "Pause filtering (stop engine)" },
        { "已开启", "on" },
        { "未开启", "off" },
        { "）。", ")." },
        { "KeyDebounce（键盘抖动过滤）", "KeyDebounce (keyboard chatter filter)" },

        // --- shared config helpers ----------------------------------------
        { "引擎已按新配置重启（PID ", "Engine restarted with the new configuration (PID " },
        { "重启引擎失败：", "Failed to restart the engine: " },
        { "已停止 ", "Stopped " },
        { " 个引擎进程。", " engine process(es)." },
        { "引擎本来就没在运行。", "The engine was not running." },
        { "引擎已经在运行。", "The engine is already running." },
        { "引擎已启动（PID ", "Engine started (PID " },
        { "启动失败：", "Start failed: " },

        // --- key names ----------------------------------------------------
        { "鼠标左键", "Mouse left" },
        { "鼠标右键", "Mouse right" },
        { "鼠标中键", "Mouse middle" },
        { "鼠标前进键", "Mouse forward" },
        { "鼠标后退键", "Mouse backward" },
        { "滚轮方向抖动", "Wheel direction bounce" },
        { "不过滤", "not filtered" },
        { "拦截  ", "Intercept  " },
        { "  拦下一次抖动：距上次 ", "  suppressed a bounce: " },
        { "ms  < 阈值 ", " ms before  < threshold " },

        // --- prompts / dialogs --------------------------------------------
        { "确定", "OK" },
        { "取消", "Cancel" },
        { "确认", "Confirm" },
        { "完成", "Done" },
        { "先选一行。", "Select a row first." },
        { "请输入 1–1000 之间的整数。", "Enter an integer between 1 and 1000." },
        { "无法保存", "Cannot save" },
        { "配置看起来没问题。", "The configuration looks fine." },
        { "校验", "Validate" },
        { "(读取失败：", "(read failed: " },

        // --- filter-keys tab ----------------------------------------------
        { "阈值单位毫秒，只作用于列在这里的键；其它键完全不受影响（global_chatter = 0）。\r\n",
          "Thresholds are in milliseconds and apply only to the keys listed here; every other key is untouched (global_chatter = 0).\r\n" },
        { "抖动反弹通常 5–30ms，正常连按通常 ≥95ms —— 取两者中间的值。",
          "Bounce is typically 5–30 ms and deliberate repeats are usually ≥95 ms — pick a value in between." },
        { "按键", "Key" },
        { "阈值 (ms)", "Threshold (ms)" },
        { "配置项", "Config entry" },
        { "来源", "Source" },
        { "鼠标", "mouse" },
        { "键盘", "keyboard" },
        { "添加按键…", "Add key…" },
        { "改阈值…", "Edit threshold…" },
        { "删除选中", "Remove selected" },
        { "重新载入", "Reload" },
        { "保存并重启引擎", "Save & restart" },
        { "提示：不要过滤 Shift / Ctrl / Alt —— 会影响所有快捷键；鼠标抖动可用「添加按键」里的下拉框。",
          "Do not filter Shift / Ctrl / Alt — it would break every keyboard shortcut. Mouse buttons are in the Add key dialog." },
        { "「", "“" },
        { "」是修饰键，过滤它会让 Ctrl+C 这类", "” is a modifier key; filtering it makes fast Ctrl+C-style" },
        { "组合键在快速按动时失效。仍然要加吗？", " shortcuts fail. Add it anyway?" },
        { "已加入 ", "Added " },
        { " 个按键（阈值默认 50ms）。\r\n改好阈值后点「保存并重启引擎」。",
          " key(s) with the default threshold of 50 ms.\r\nAdjust the thresholds, then click “Save & restart engine”." },
        { "把「", "Set “" },
        { "」的阈值改成多少毫秒？", "” to which threshold in milliseconds?" },
        { "\r\n建议：明显大于抖动值、明显小于你最快的连续两次按键间隔。",
          "\r\nRule of thumb: clearly above the measured bounce, clearly below your fastest deliberate repeat." },
        { "先选要删的行（可多选）。", "Select the rows to delete (multiple allowed)." },
        { "现在没有任何过滤按键，等于关闭过滤。确定保存吗？",
          "No keys are filtered right now, which means filtering is off. Save anyway?" },
        { "按键配置已更新（", "key configuration updated (" },
        { " 个过滤按键）", " filtered key(s))" },
        { "保存失败：", "Save failed: " },
        { "已保存。\r\n", "Saved.\r\n" },
        { "找不到 ", "Cannot find " },

        // --- key capture dialog -------------------------------------------
        { "按一下要过滤的键", "Press the key to filter" },
        { "请按一下想过滤的键（可连续按多个，全部会被加入）。\r\n",
          "Press a key you want to filter (press several in a row; all of them are added).\r\n" },
        { "这里按下的键只被记录，不会被输入到其它程序。Esc 结束后点「完成」。",
          "Keys pressed here are only recorded, never typed into other programs. Esc finishes, then click Done." },
        { "加入鼠标键", "Add mouse button" },
        { "先选一个鼠标目标。", "Pick a mouse target first." },
        { "提示：按 Esc 会结束采集，Esc 本身不会被加入过滤。",
          "Esc ends the capture and is never added to the filter." },

        // --- chatter scan tab ---------------------------------------------
        { "点「开始扫描」，然后像平时一样打字，并特意多按几次你怀疑的按键（每次一下，约 20–30 次）。\r\n",
          "Click “Start scan”, type as usual, and press the suspect key 20–30 times, one press at a time.\r\n" },
        { "扫描看的是键盘原始信号（在过滤之前），所以能发现还没被加进过滤的抖动键。",
          "The scan sees the raw keyboard signal (before filtering), so it finds keys that are not filtered yet." },
        { "按下次数", "Presses" },
        { "<20ms", "<20ms" },
        { "20–50ms", "20–50ms" },
        { "最短间隔", "Shortest gap" },
        { "判定", "Verdict" },
        { "判定规则：同一个键两次按下间隔 < 40ms，物理上不可能，出现即判为抖动。",
          "Rule: a same-key press-to-press gap under 40 ms is physically impossible for a human, so it counts as chatter." },
        { "开始扫描", "Start scan" },
        { "停止扫描", "Stop scan" },
        { "把疑似抖动的键加入过滤", "Add flagged keys to filter" },
        { "清空结果", "Clear results" },
        { "扫描中…现在请打字，并多按几次怀疑的按键。",
          "Scanning… type now and press the suspect key several times." },
        { "疑似抖动 — 建议过滤", "Chattering — filter it" },
        { "正常", "normal" },
        { "（本次含 ", "(this run included " },
        { " 次软件注入的按键，只有测试才会出现；真实手按不会有。）",
          " software-injected keys, which only happen in tests; real typing never does.)" },
        { "没有发现需要新加的抖动按键。", "No chattering keys were found to add." },
        { "将加入（阈值 50ms）：", "Will add (threshold 50 ms): " },
        { "\r\n\r\n保存并重启引擎？", "\r\n\r\nSave and restart the engine?" },
        { "扫描加入过滤按键：", "scan added filter keys: " },
        { "、", ", " },

        // --- main window ---------------------------------------------------
        { "KeyDebounce — 主界面", "KeyDebounce — Main Window" },
        { "概览", "Overview" },
        { "过滤按键", "Filter Keys" },
        { "抖动扫描", "Chatter Scan" },
        { "日志", "Log" },
        { "配置", "Config" },
        { "启动过滤", "Start filtering" },
        { "停止过滤", "Stop filtering" },
        { "重启引擎", "Restart engine" },
        { "停止后键盘抖动会重新出现（直到再次启动）。确定停止吗？",
          "Chatter comes back until you start it again. Stop anyway?" },
        { "用记事本打开配置", "Open config.txt" },
        { "打开程序文件夹", "Open program folder" },
        { "实时拦截镜像（与引擎同规则，在过滤前取样）",
          "Live intercept mirror (engine's rule, sampled before filtering)" },
        { "阈值", "Threshold" },
        { "放行的按下", "Presses allowed" },
        { "拦下的抖动", "Bounces blocked" },
        { "最近一次拦截", "Last intercept" },
        { "自动滚动到最新", "Auto-scroll to newest" },
        { "清空显示", "Clear view" },
        { "打开日志文件", "Open log file" },
        { "记录：启停与配置变更、以及每一次被拦下的抖动（含间隔毫秒）。",
          "Records start/stop, configuration changes, and every suppressed bounce with its gap in ms." },
        { "高级：直接编辑 config.txt。保存前会校验 key. 名称（写错会让引擎启动时弹错误框）。",
          "Advanced: edit config.txt directly. key. names are validated before saving (an invalid name makes the engine pop an error dialog at startup)." },
        { "配置文件被直接编辑并保存", "config.txt was edited and saved directly" },
        { "缺少冒号：", "missing colon: " },
        { "按键名无效：", "invalid key name: " },
        { "阈值不是 0–1000 的整数：", "threshold is not an integer 0–1000: " },
        { "发现 ", "Found " },
        { " 处问题，保存被拒绝：\r\n\r\n  ", " problem(s), save refused:\r\n\r\n  " },
        { "\r\n\r\n（引擎遇到无效按键名会在启动时弹错误框，所以这里先拦住。）",
          "\r\n\r\n(The engine pops an error dialog at startup for invalid key names, so this check blocks it early.)" },
        { "● 正在过滤", "● Filtering" },
        { "● 未在过滤", "● Not filtering" },
        { "引擎：KeyboardChatterBlocker", "Engine: KeyboardChatterBlocker" },
        { "（PID ", " (PID " },
        { "）", ")" },
        { "（未运行）", " (not running)" },
        { "    过滤按键：", "    filtered keys: " },
        { " 个    计时基准：", "    measure from: " },
        { "\r\n配置：", "\r\nConfig: " },
        { "\r\n开机自启：", "\r\nAutostart: " },
        { "    托盘图标：", "    tray icon: " },
        { "绿色", "green" },
        { "红色", "red" },
        { "尚未生成", "not created yet" },
        { "已生成但为空（引擎退出时才写入）", "exists but empty (the engine writes it on exit)" },
        { " 更新", " updated" },
        { "「拦下的抖动」由本程序按引擎规则实时镜像得出；引擎自身只在退出或改配置时写 ",
          "“Bounces blocked” is mirrored live by this app using the engine's rule; the engine itself only writes " },
        { "blocker_stats.csv（当前：", "blocker_stats.csv when it exits or saves (currently: " }
    };
}
