# Security policy

## Reporting a vulnerability

Please report security issues **privately**, via GitHub's
[Security → Report a vulnerability](https://docs.github.com/en/code-security/security-advisories/guidance-on-reporting-and-writing-information-about-vulnerabilities)
advisory form on this repository. If that is unavailable, open a normal issue that
contains no exploit details and ask for a private channel.

Useful reports for this project: anything that lets another local user or a
lower-privileged process abuse KeyDebounce, path/argument injection into how the
launcher starts the engine, a way to make the filter block keys it should not, or
unintended information disclosure (see the logging notes below).

## What this software touches

Being explicit about the blast radius is part of the design:

| Area | Behaviour |
|---|---|
| Kernel driver | **None.** Pure user-space (`WH_KEYBOARD_LL` hook + WinForms). |
| Service / scheduled task | **None.** |
| Registry | **No values written.** (Windows itself records tray-icon metadata under `HKCU\Control Panel\NotifyIconSettings` for every notification icon, as it does for any app.) |
| Files written | `config.txt`, `KeyDebounce.log`, `KeyDebounce.status`, `blocker_stats.csv`, and a `chatter.wav`-free folder otherwise — all inside the program folder, except the one optional Startup shortcut. |
| Network | **None.** There is no HTTP client, socket, updater or telemetry code in the project. |
| Privileges | Runs as the invoking user. No elevation is requested. It can be run elevated deliberately, which widens key coverage to elevated windows. |
| Keyboard access | A global low-level keyboard hook. Keystrokes are inspected for timing only; the app never stores what you typed (see below). |

## Logging and privacy

`KeyDebounce.log` records, per line: a timestamp, the **name of a filtered key** whose
press was suppressed as bounce, and the measured gap in milliseconds. It also records
start/stop/config-change events.

That means the log is *key-timing metadata* for the keys you chose to filter — not
text, not unfiltered keys, not window titles, not clipboard data. If that matters to
you, delete `KeyDebounce.log` while the app is not running, or keep the folder on an
encrypted volume. The same applies to `blocker_stats.csv`, which the engine writes on
exit (key name, threshold, count).

## The upstream engine binary

`KeyboardChatterBlocker.exe` is a third-party binary (MIT, Frenetic LLC) that this
project starts but does not modify. It is **not** committed to this repository; the
release archive ships it with its license reproduced in `THIRD-PARTY-NOTICES.md`,
along with the SHA-256 hash and the expected Authenticode signer so you can verify it
yourself before running it. A copy whose signature is not `CN=Frenetic LLC` should be
distrusted.

## Anti-cheat false positives

A global low-level keyboard hook is the same mechanism input-automation tools use.
Some kernel-level anti-cheat systems therefore react to this program (the upstream
engine's README documents a VAC-ban report from a similar tool). Prefer
`auto_disable_programs` or `auto_disable_on_fullscreen` for competitive online games.
This is a compatibility/safety caveat rather than a vulnerability, and it is not
fixable in user space.
