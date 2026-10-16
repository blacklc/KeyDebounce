# KeyDebounce — Usage

Per-key keyboard chatter filter for Windows. This document is the reference for
the main window, the tray menu, `config.txt`, and what to do when a new key
starts double-typing.

> **UI language:** the interface is bilingual — English and Chinese — selected
> automatically from your Windows UI language, and forceable with `--en` / `--zh`
> (e.g. `KeyDebounce.exe --ui --en`). Both wordings are given below as
> **English (中文)** so the document works in either mode. The product name, file
> names, tray tooltip, status file and log prefix are always English.

---

## 1. How the filter decides

A worn switch's contacts bounce for a few milliseconds, so one physical press
becomes `down, up, down, up, down, up`. KeyDebounce drops the *extra presses* and
keeps one:

- Each filtered key has its own threshold (ms) and the engine compares the new
  press against the last **accepted** press (or release) for that key.
- A press closer than the threshold to that reference is treated as bounce and
  discarded. Its release is discarded with it, and — importantly — a discarded
  press does **not** reset the clock.
- `measure_from: Release` (recommended for bounce-after-release patterns) counts
  from the last accepted *release*; `Press` counts from the last accepted *press*.
- Keys that are not listed **are not touched at all** (`global_chatter: 0`), so
  the filter adds zero latency to ordinary typing.

---

## 2. Main window

Tray icon → double-click, or tray menu → `打开主界面（配置 / 日志 / 扫描）`.

### Tab `概览` — Overview

- Big status line: **● 正在过滤 / ● 未在过滤** (filtering / not filtering), green
  or red.
- Detail block: engine name + PID, number of filtered keys, `measure_from`,
  config path, start-with-Windows state, tray icon colour.
- Buttons: `启动过滤` (start), `停止过滤` (stop), `重启引擎` (restart engine),
  `用记事本打开配置` (open config in Notepad), `打开程序文件夹` (open folder).
- **Live intercept mirror** — per key: threshold, presses that were let through,
  bounces suppressed, and the time of the last intercept. This is measured by
  KeyDebounce itself (see §5), not by the engine, which is why it updates live.

### Tab `过滤按键` — Filter Keys

- Table of the filtered keys: key, threshold (ms), `config.txt` entry, keyboard/mouse.
- `添加按键…` — a dialog asks you to **press the key**; press as many as you like
  (mouse buttons are available from a dropdown). Esc finishes. The captured key
  name is always the exact name the engine accepts.
- `改阈值…` — change the threshold of the selected key.
- `删除选中` — remove selected keys from the filter.
- `保存并重启引擎` — writes `config.txt` and restarts the engine (required,
  the engine only reads its config at startup).
- Warning: do **not** filter `LShiftKey` / `LControlKey` / `LMenu` etc. Filtering
  a modifier breaks fast Ctrl+C-style combos for every application.

### Tab `抖动扫描` — Chatter scan

- `开始扫描` (start) / `停止扫描` (stop) / `清空结果` (clear) / `把疑似抖动的键加入过滤`
  (adopt suspicious keys).
- Sees the **raw** keyboard signal (before filtering), so it can find keys that
  are not filtered yet.
- Verdict rule: any same-key press-to-press gap **< 40 ms** is physically
  impossible for a human → flagged red.
- Rows injected by software are counted separately and called out in the footer,
  so test noise cannot be mistaken for real chatter.

### Tab `日志` — Log

- Live tail of `KeyDebounce.log`: launcher events (start/stop/restart, config
  edits, self-heal) **and every suppressed bounce with its measured gap**, e.g.
  `KeyDebounce  拦截  E  拦下一次抖动：距上次 8.0ms  < 阈值 50ms`.
- `自动滚动到最新` (auto-scroll), `重新载入`, `清空显示`, `打开日志文件`.

### Tab `配置` — Config

- The raw `config.txt`, editable.
- `校验` (validate) and `保存并重启引擎` (save + restart). Validation refuses an
  invalid `key.` name — the engine shows a **modal error dialog** at startup for
  unknown key names, so this guard exists to keep the engine startable.

---

## 3. Tray menu

| Item | Meaning |
|---|---|
| `状态：正在过滤（阈值 50ms）` | Live state. |
| `暂停过滤 / 开始过滤` | Temporary off/on. A manual pause is respected — self-heal will not override it. |
| `重启引擎` | Restart the engine (picks up `config.txt` changes made outside the UI). |
| `开机自启` ✓ | Start with Windows (shortcut in the Startup folder). |
| `打开配置文件（改阈值）` | Open `config.txt` in Notepad. |
| `打开使用说明` | Open this document (`USAGE.md`). |
| `打开安装文件夹` | Open the program folder. |
| `打开主界面（配置 / 日志 / 扫描）` | The main window. |
| `查看实时日志` | Main window, Log tab. |
| `关于 / 当前状态` | Summary dialog: state, config path, autostart, tray colour. |
| `退出` | Quit. You are asked whether to stop the engine too. |

**Self-healing:** the launcher checks the engine every second. If it died
(crash, antivirus, Task Manager) it is restarted within ~10 s and the log records
`引擎未运行且未手动暂停 -> 自动重启`. The filter is therefore alive from boot to
shutdown, in every application.

---

## 4. `config.txt` reference

Written by the launcher; the engine rewrites it in its own order when it saves
(comments and blank lines are dropped — put comments elsewhere if you care).
Settings used by this install:

```ini
is_enabled: true            # master switch for the engine
global_chatter: 0           # 0 = every key NOT listed below is untouched
minimum_chatter_time: 0     # ignore 0 ms "hits" (buggy inputs only)
key.E: 50                   # one filtered key: E, 50 ms
measure_from: Release       # count from the last accepted release
hide_in_system_tray: true   # engine shows only a tray icon, no window
save_stats: true            # let the engine keep blocker_stats.csv
exclude_injected: false     # keep false so other software's keys are filtered too
hotkey_toggle: win + shift + k   # global on/off hotkey
```

Other engine settings you can add (`<name>: <value>` per line):

| Setting | Effect |
|---|---|
| `key.<Name>` | Per-key threshold in ms. `key.A`, `key.F5`, `key.D1`, `key.OemComma`, `key.LShiftKey`, … plus the mouse pseudo-keys `mouse_left`, `mouse_right`, `mouse_middle`, `mouse_forward`, `mouse_backward`, `wheel_change`. Names are the standard .NET `Keys` enum names, case-insensitive. |
| `auto_disable_programs` | Slash-separated executable names; the filter turns off while any of them run. |
| `auto_disable_on_fullscreen` | `true` = turn off whenever a fullscreen app is focused. |
| `hotkey_enable` / `hotkey_disable` / `hotkey_tempenable` / `hotkey_tempdisable` / `hotkey_showform` | Further global hotkeys. |
| `other_key_resets_timeout` | Engine's own extra option. |

**Key names are strict.** An unknown name makes the engine pop an error dialog at
startup. Use the UI's key picker (`添加按键…`) or the `校验` button instead of
guessing — both only ever write names the engine accepts.

---

## 5. The live intercept mirror, and why it exists

The engine writes `blocker_stats.csv` only when it saves its own config, so that
file cannot drive a live view. KeyDebounce therefore installs its **own**
keyboard hook *after* the engine's. Windows calls hook procedures newest-first,
so this hook sees the stream **before** the engine filters it, replays the
engine's rule, and reports exactly which presses were suppressed and by how many
milliseconds. That is the source of the `概览` counters and the `日志` intercept
lines.

---

## 6. When a new key starts double-typing

1. Main window → `抖动扫描` → `开始扫描`, then type and hammer the suspect key
   ~20–30 times, one press at a time.
2. If it is flagged red: `把疑似抖动的键加入过滤` → confirm (adds it at 50 ms).
3. Still double-typing after that? `过滤按键` → `改阈值…` → raise by 10–20 ms →
   `保存并重启引擎`.
4. Watch `概览` while you type: `放行的按下` counts presses, `拦下的抖动` counts the
   bounces that were stopped.
5. Re-scan every few months. The bounce window widens as the switch wears; once
   it approaches your fastest deliberate repeat, no threshold separates them any
   more and the switch has to be replaced.

---

## 7. Troubleshooting

| Symptom | Cause / fix |
|---|---|
| Tray icon is not visible | Windows 11 overflow `^`. Drag it onto the taskbar, or Settings → Personalisation → Taskbar → Other system tray icons. |
| Status says `未在过滤` (not filtering) | The engine is not running: tray menu → `开始过滤`, or `概览` → `启动过滤`. Check the log for antivirus blocking the engine. |
| Still double-typing | Threshold too low for that switch (raise it), or the key is not in the filter list at all, or you are typing into an elevated app / the secure desktop (see §8). |
| Characters going missing | Threshold too high — it is eating your deliberate repeats. Lower it. |
| A game behaves oddly, or anti-cheat complains | A global keyboard hook is indistinguishable from input automation to some anti-cheat systems. Add the game to `auto_disable_programs`, or `auto_disable_on_fullscreen: true`, then restart the engine. |
| Two engines running | Only start the engine via the launcher: two engines mean two keyboard hooks. Kill all `KeyboardChatterBlocker` processes and use the tray menu. |
| Engine error dialog about a key name | An invalid `key.` line was written outside the UI. Fix it in the `配置` tab (it validates), or delete the line. |

---

## 8. Limitations (as designed, not bugs)

1. **User-space only.** Input in the secure desktop (UAC prompt, logon screen) is
   not filtered. Use a PIN login if your password contains a chattering key.
2. **Raw Input programs.** Some modern games read the keyboard through the Raw
   Input API; a user-space hook cannot filter that. Programs using normal
   keyboard messages (including World of Warcraft) are covered.
3. **Elevated applications.** A non-elevated hook does not see keys typed into
   administrator-level windows. Run `KeyDebounce.exe` as administrator if you
   need coverage there (the launcher, engine and UI all inherit it).
4. **Anti-cheat risk.** This app installs a global low-level keyboard hook — the
   same technique input automation uses. The upstream project's README carries a
   report of a VAC ban from running a similar tool. Prefer the per-game
   auto-disable settings for competitive online games.
5. **Software cannot outrun wear.** It hides the symptom. The switch keeps
   wearing, and the bounce window keeps widening. The durable fix is a new switch
   (minutes on a hot-swap board, soldering otherwise).

---

## 9. Quick reference

```
Folder            D:\Tools\KeyDebounce
Start             KeyDebounce.exe  (tray)          ← only entry point
Engine            KeyboardChatterBlocker.exe       ← managed by the launcher
Main window       double-click the tray icon, or KeyDebounce.exe --ui
Tab shortcuts     --manage (filter keys)  --scan (chatter scan)
                  --log (live log)        --config (raw config)
Config            config.txt                       ← per-key thresholds
Live state        KeyDebounce.status               ← e_key_filter / tray_icon / threshold_ms / autostart
Logs              KeyDebounce.log                  ← launcher events + every intercept
Diagnostics       blocker_stats.csv                ← the engine's own counters
Rebuild           src\build.cmd                    ← uses the in-box .NET compiler
Uninstall         tray → Quit (stop engine) → delete folder → delete Startup shortcut
```
