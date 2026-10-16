# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.1] — 2026-10-16

### Fixed

- **Intercept recording stopped as soon as the main window was closed.** The mirror
  (and its keyboard hook) used to live in the UI process, so `KeyDebounce.log` only
  gained "suppressed bounce" lines while a window happened to be open, and the counters
  reset every time the window was reopened. The mirror now runs in the **tray
  launcher** — it re-installs its hook whenever the engine (re)starts, so it stays the
  newest hook in the chain and keeps observing the **unfiltered** stream — and
  publishes live counters to `KeyDebounce.stats`, which is what the Overview tab
  displays. Closing the window no longer affects recording or counters.
- Log tail view read the entire file on every refresh and re-rendered lines that had
  come from the file; it now reads only the last 64 KB and shows what is on disk.
- Log rotation at 2 MB (`KeyDebounce.log.1`), because the log now grows for the whole
  boot-to-shutdown session.
- A busy key is no longer dropped from the log after 200 lines: the first 200 are
  logged in full, then at most one line per second per key.

## [1.0.0] — 2026-10-16

First public release. The project started as a single-purpose fix for one chattering
`E` key on a CHERRY `046A:00E3` keyboard and was generalised into a per-key filter.

### Added

- **Tray launcher** (`KeyDebounce.exe`) around the KeyboardChatterBlocker engine:
  start / stop / restart, a status icon whose colour is the engine's real state
  (green = filtering, red = stopped), and a "start with Windows" shortcut toggle.
- **Self-healing engine supervision** — if the engine dies (crash, antivirus, Task
  Manager) it is restarted within ~10 s, and a deliberate user pause is respected.
  One instance only: a second engine would install a second keyboard hook.
- **Main window with five tabs** — Overview, Filter Keys, Chatter Scan, Log, Config.
- **Per-key configuration** through the UI: add keys by pressing them (mouse buttons
  included), per-key thresholds, save-and-restart in one action. Key names are
  validated before saving because the engine shows a modal error for unknown names.
- **Chatter scanner** — watches the raw keyboard signal (before filtering) and flags
  any key with a sub-40 ms press-to-press gap, i.e. a key that has started to bounce.
- **Live intercept mirror** — per-key presses allowed vs bounces blocked, and a log
  line per suppressed bounce with its measured gap in milliseconds. Implemented with a
  second keyboard hook installed after the engine's, exploiting the fact that Windows
  calls hook procedures newest-first.
- **Bilingual UI** — English and Chinese, auto-selected from the system UI language,
  overridable with `--en` / `--zh`.
- **Machine-readable status file** (`KeyDebounce.status`) and a plain-text log.
- **Documentation**: `README.md`, `INSTALL.md`, `USAGE.md`, `SECURITY.md`, this
  changelog, and `THIRD-PARTY-NOTICES.md` reproducing the engine's MIT license.
- **Development tooling** in `tools/`: a Raw Input chatter probe, a capture analyser,
  a hook-chain verifier that proves what reaches applications, the i18n string
  wrapper/validator, and a window screenshot helper.

### Security / privacy notes

- No network code, no telemetry, no auto-update, no analytics.
- No kernel driver, no service, no registry values; the only system change is one
  optional Startup shortcut.
- The log records *suppressed-key metadata only* (key name, gap in ms, time) — see
  `SECURITY.md`.

### Known limitations (by design)

- User-space hooks cannot filter the secure desktop (UAC / logon screen) and do not see
  keys typed into elevated windows unless this app is elevated too.
- Programs reading the keyboard via the Raw Input API cannot be filtered.
- A global keyboard hook may be treated as input automation by kernel-level
  anti-cheat; use `auto_disable_programs` / `auto_disable_on_fullscreen` for those games.
- Software cannot compensate for a switch whose bounce has grown past the user's
  fastest deliberate repeat — at that point the switch must be replaced.
