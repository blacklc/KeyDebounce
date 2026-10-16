# KeyDebounce — Installation

A per-key **keyboard chatter (switch bounce) filter** for Windows.
One physical keypress that a worn switch reports as two or three presses gets
reduced back to one — for the keys you choose, and only those keys.

KeyDebounce is a launcher/UI around the open-source engine
[KeyboardChatterBlocker](https://github.com/FreneticLLC/KeyboardChatterBlocker)
(MIT, v1.21). It adds: a tray status icon, start-with-Windows, self-healing if
the engine dies, a key manager, a chatter scanner, a live intercept log and a
machine-readable status file.

---

## 1. Requirements

| | |
|---|---|
| OS | Windows 7 / 8 / 10 / 11 (x64 or ARM64) |
| Runtime | .NET Framework 4.7.2 or newer — **already present on Windows 10/11** |
| Privileges | none — no administrator rights, no kernel driver, no service, no registry changes |
| Hardware | any keyboard that presents itself as a normal HID keyboard |

---

## 2. What you get (the `KeyDebounce` folder)

| File | Role |
|---|---|
| **`KeyDebounce.exe`** | **The entry point.** Tray app: status icon, start/stop/restart, start-with-Windows, self-heal, and the main window (`配置/日志/扫描`). |
| `KeyboardChatterBlocker.exe` | The actual filtering engine (upstream, unmodified). **Do not start it by itself** — the launcher manages it. |
| `config.txt` | Engine configuration. Per-key thresholds live here. |
| `KeyDebounce.log` | Launcher log: starts, engine start/stop, self-heal, config edits, every intercept with its gap in ms. |
| `KeyDebounce.status` | One-glance state: `e_key_filter=running`, `tray_icon=green`, `threshold_ms=50`, `autostart=on`. |
| `blocker_stats.csv` | The engine's own cumulative counters (written when the engine saves/exits). |
| `USAGE.md` / `使用说明.txt` | Usage documentation. |
| `src\` | Full C# source (`KeyDebounce.cs`, `KeyDebounce.Ui.cs`) + `build.cmd`. |

The folder is **fully portable**: no installer, no files written outside it
apart from one shortcut in your Startup folder.

---

## 3. Install (2 minutes)

1. **Copy the whole `KeyDebounce` folder** anywhere you like — `D:\Tools\KeyDebounce`,
   a USB stick, your Desktop. Do not split the files up: the launcher finds the
   engine and `config.txt` next to itself.
2. **Double-click `KeyDebounce.exe`.**
   On first run it will:
   - create `config.txt` if it is missing (with sane defaults),
   - start `KeyboardChatterBlocker.exe` and keep it running,
   - show a tray icon in the notification area.
3. **Look at the tray icon** (bottom-right). Green circle with a white `E` =
   filtering. Red = not filtering.
4. **Pick which keys to filter** — open the main window (double-click the tray
   icon) → tab `过滤按键` (Filter Keys) → `添加按键…` → press the key. Done; the
   default threshold is 50 ms.
5. **Start with Windows** — tray icon → `开机自启` (Start with Windows) to
   toggle. It is a plain shortcut in
   `…\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup\KeyDebounce.lnk`.
   (This installation already has it enabled.)

### Windows 11: the tray icon hides by default

Windows 11 parks new notification icons in the `^` overflow flyout. To pin it to
the visible area: open `^` and **drag the green icon onto the taskbar**, or go to
Settings → Personalisation → Taskbar → **Other system tray icons** → enable
*KeyDebounce*. The engine has its own icon in that same list (that one opens the
engine's own window, which shows the engine's built-in chatter log).

---

## 4. Verify it actually works (30 seconds, do this once)

A running process is not proof. This is:

1. Open **Notepad**.
2. Press the filtered key (say `E`) **exactly 20 times**, deliberately — one
   press at a time, ~0.3 s apart.
3. The document must contain **exactly 20 characters**. If a worn switch were
   still leaking through you would see 25–60.
4. Now type normally, including words with two of that letter in a row
   (`see`, `keep`, `three`). No character may go missing.

Open the main window → tab `概览` (Overview) and watch `拦下的抖动` (suppressed
bounces) grow while you type, and tab `日志` (Log) for every intercept with its
measured gap in milliseconds.

---

## 5. Tuning on a different keyboard (important)

Chatter width is a property of the individual switch. **Never assume 50 ms is
right for another keyboard.** Use the built-in scanner:

1. Main window → tab `抖动扫描` (Chatter scan) → `开始扫描` (Start scan).
2. Type normally and press the suspect key **20–30 times, one press at a time**.
3. The table shows per key: presses, gaps under 20 ms, gaps 20–50 ms, shortest
   gap, verdict. Anything with a gap **under 40 ms** is physically impossible for
   a human — that key is chattering (shown in red).
4. Press `把疑似抖动的键加入过滤` (Add suspicious keys to the filter) → it writes
   `key.<Name>: 50` for each and restarts the engine.
5. If a key still double-types, raise its threshold by 10–20 ms
   (`过滤按键` → `改阈值…` → `保存并重启引擎`). Keep it clearly below your
   fastest deliberate repeat of that key.

Rule of thumb: thresholds should sit **above the measured bounce and below the
fastest legitimate repeat**. Measured example from a CHERRY `046A:00E3`: bounce
4.7–10.3 ms, fastest genuine repeat ≥ 95 ms → 50 ms is comfortably in the empty
band between them.

---

## 6. Uninstall (leaves nothing behind)

1. Tray icon → `退出` (Quit) → answer **是/Yes** when asked whether to stop the
   filter engine as well.
2. Delete the `KeyDebounce` folder.
3. Delete `KeyDebounce.lnk` from your Startup folder (listed in §3.5).

No driver, service, registry value or scheduled task is created by this app — the
only system-level change is that one shortcut.

---

## 7. Rebuilding from source (optional)

```bat
cd /d <folder>\src
build.cmd
```

`build.cmd` uses the C# compiler that ships with Windows
(`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`) — no Visual Studio,
no SDK, no NuGet. It kills a running instance first, compiles both source files,
and re-creates `KeyDebounce.exe`. Source is plain C# 5 WinForms.
