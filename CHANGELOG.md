# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
