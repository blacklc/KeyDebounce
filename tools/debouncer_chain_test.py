#!/usr/bin/env python3
"""Chain-order test for a keyboard debouncer (KeyboardChatterBlocker et al).

Windows calls low-level keyboard hooks in reverse installation order (most
recent first). So a hook installed *before* the debouncer runs *after* it in the
chain and therefore observes exactly the events the debouncer let through --
i.e. the application-layer view -- without depending on window focus at all.

This script:
  1. installs a passive WH_KEYBOARD_LL hook (never blocks, always chains on),
  2. waits `--delay` seconds (start the debouncer inside that window),
  3. injects the bounce trace measured on the real faulty key,
  4. reports how many E key-downs arrived downstream.

  3 downs downstream -> nothing is filtering
  1 down downstream  -> the debouncer swallowed both bounces, first press intact

Usage: python kcb_chain_test.py --delay 8 --label "blocker ON"
"""
import argparse
import ctypes
import ctypes.wintypes as wt
import os
import subprocess
import threading
import time

user32 = ctypes.WinDLL("user32", use_last_error=True)
kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)

WH_KEYBOARD_LL = 13
WM_KEYDOWN, WM_KEYUP = 0x0100, 0x0101
WM_SYSKEYDOWN, WM_SYSKEYUP = 0x0104, 0x0105
VK_E = 0x45
INPUT_KEYBOARD, KEYEVENTF_KEYUP = 1, 0x0002


class KBDLLHOOKSTRUCT(ctypes.Structure):
    _fields_ = [("vkCode", wt.DWORD), ("scanCode", wt.DWORD),
                ("flags", wt.DWORD), ("time", wt.DWORD),
                ("dwExtraInfo", ctypes.POINTER(wt.ULONG))]


class MOUSEINPUT(ctypes.Structure):
    _fields_ = [("dx", wt.LONG), ("dy", wt.LONG), ("mouseData", wt.DWORD),
                ("dwFlags", wt.DWORD), ("time", wt.DWORD),
                ("dwExtraInfo", ctypes.POINTER(wt.ULONG))]


class KEYBDINPUT(ctypes.Structure):
    _fields_ = [("wVk", wt.WORD), ("wScan", wt.WORD), ("dwFlags", wt.DWORD),
                ("time", wt.DWORD), ("dwExtraInfo", ctypes.POINTER(wt.ULONG))]


class _U(ctypes.Union):
    # both members must be present or INPUT is 32 bytes and SendInput fails
    _fields_ = [("mi", MOUSEINPUT), ("ki", KEYBDINPUT)]


class INPUT(ctypes.Structure):
    _fields_ = [("type", wt.DWORD), ("u", _U)]


LRESULT = ctypes.c_ssize_t
HOOKPROC = ctypes.WINFUNCTYPE(LRESULT, ctypes.c_int, wt.WPARAM, wt.LPARAM)
user32.SetWindowsHookExW.restype = wt.HHOOK
user32.SetWindowsHookExW.argtypes = [ctypes.c_int, HOOKPROC, wt.HINSTANCE,
                                     wt.DWORD]
user32.CallNextHookEx.restype = LRESULT
user32.CallNextHookEx.argtypes = [wt.HHOOK, ctypes.c_int, wt.WPARAM, wt.LPARAM]
user32.PeekMessageW.argtypes = [ctypes.POINTER(wt.MSG), wt.HWND, wt.UINT,
                                wt.UINT, wt.UINT]
user32.SendInput.argtypes = [wt.UINT, ctypes.c_void_p, ctypes.c_int]
user32.SendInput.restype = wt.UINT

seen = []          # (t, msg, flags) for E only


def hook_proc(n_code, w_param, l_param):
    if n_code == 0:
        kb = ctypes.cast(l_param, ctypes.POINTER(KBDLLHOOKSTRUCT)).contents
        if kb.vkCode == VK_E:
            seen.append((time.perf_counter(), w_param, kb.flags))
    return user32.CallNextHookEx(None, n_code, w_param, l_param)


HOOK = HOOKPROC(hook_proc)     # keep a reference or it gets garbage collected


def send(vk, up=False):
    inp = INPUT(type=INPUT_KEYBOARD)
    inp.u.ki = KEYBDINPUT(wVk=vk, wScan=0,
                          dwFlags=KEYEVENTF_KEYUP if up else 0, time=0,
                          dwExtraInfo=None)
    if user32.SendInput(1, ctypes.byref(inp), ctypes.sizeof(INPUT)) != 1:
        raise ctypes.WinError(ctypes.get_last_error())


def delay_ms(ms):
    end = time.perf_counter() + ms / 1000.0
    while time.perf_counter() < end:
        time.sleep(0.0005)


def replay_trace():
    """The real measured fault: one press reported as three (ms after press)."""
    send(VK_E); delay_ms(12.26)          # down
    send(VK_E, up=True); delay_ms(7.24)  # up
    send(VK_E); delay_ms(15.26)          # bounce down
    send(VK_E, up=True); delay_ms(9.53)  # up
    send(VK_E); delay_ms(40.6)           # bounce down
    send(VK_E, up=True)                  # up


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--label", default="run")
    ap.add_argument("--start-blocker", metavar="EXE",
                    help="launch this debouncer AFTER installing our hook, so "
                         "its hook is more recent and therefore runs first in "
                         "the chain (we then see only what it passes through)")
    ap.add_argument("--settle", type=float, default=4.0,
                    help="seconds to let the debouncer start and hook in")
    ap.add_argument("--stop-blocker", action="store_true",
                    help="kill the debouncer when done")
    ap.add_argument("--observe", type=float, metavar="SECONDS",
                    help="do not inject anything: just log the E events that "
                         "reach us (downstream of the debouncer) for this many "
                         "seconds. Run a raw-input capture in parallel to see "
                         "everything the keyboard produced.")
    args = ap.parse_args()

    hook = user32.SetWindowsHookExW(WH_KEYBOARD_LL, HOOK, None, 0)
    if not hook:
        raise ctypes.WinError(ctypes.get_last_error())
    print(f"[{args.label}] observer hook installed (it will run LAST in the "
          f"chain)", flush=True)

    blocker = None
    if args.start_blocker:
        exe = args.start_blocker
        cwd = os.path.dirname(exe) or "."
        blocker = subprocess.Popen([exe], cwd=cwd)
        print(f"[{args.label}] started {os.path.basename(exe)} (pid "
              f"{blocker.pid}), waiting {args.settle}s for it to hook in",
              flush=True)
        time.sleep(args.settle)

    if args.observe:
        print(f"[{args.label}] observing for {args.observe}s -- press the "
              f"suspect key now", flush=True)
        deadline = time.perf_counter() + args.observe
        msg = wt.MSG()
        while time.perf_counter() < deadline:
            while user32.PeekMessageW(ctypes.byref(msg), None, 0, 0, 1):
                user32.TranslateMessage(ctypes.byref(msg))
                user32.DispatchMessageW(ctypes.byref(msg))
            time.sleep(0.002)
        user32.UnhookWindowsHookEx(hook)
        downs = [s for s in seen if s[1] in (WM_KEYDOWN, WM_SYSKEYDOWN)]
        ups = [s for s in seen if s[1] in (WM_KEYUP, WM_SYSKEYUP)]
        t0 = seen[0][0] if seen else time.perf_counter()
        print(f"[{args.label}] SURVIVORS: {len(downs)} E key-downs, "
              f"{len(ups)} E key-ups")
        for t, m, f in seen:
            kind = "down" if m in (WM_KEYDOWN, WM_SYSKEYDOWN) else "up  "
            print(f"[{args.label}]   t+{t - t0:8.3f}s {kind} flags=0x{f:02X}")
        if len(downs) > 1:
            gaps = [(b[0] - a[0]) * 1000 for a, b in zip(downs, downs[1:])]
            print(f"[{args.label}] surviving down-to-down gaps (ms): "
                  f"{[round(g, 1) for g in gaps]}")
        if args.stop_blocker and blocker:
            subprocess.run(["taskkill", "/IM", os.path.basename(
                args.start_blocker), "/F"], capture_output=True)
        return

    print(f"[{args.label}] injecting the measured bounce trace now", flush=True)
    replay_trace()

    deadline = time.perf_counter() + 1.5
    msg = wt.MSG()
    while time.perf_counter() < deadline:
        while user32.PeekMessageW(ctypes.byref(msg), None, 0, 0, 1):
            user32.TranslateMessage(ctypes.byref(msg))
            user32.DispatchMessageW(ctypes.byref(msg))
        time.sleep(0.002)
    user32.UnhookWindowsHookEx(hook)

    downs = [s for s in seen if s[1] in (WM_KEYDOWN, WM_SYSKEYDOWN)]
    ups = [s for s in seen if s[1] in (WM_KEYUP, WM_SYSKEYUP)]
    injected = sum(1 for s in seen if s[2] & 0x10)
    print(f"[{args.label}] downstream E key-downs: {len(downs)}  "
          f"key-ups: {len(ups)}  (injected flag on {injected} events)")
    for t, m, f in seen:
        kind = "down" if m in (WM_KEYDOWN, WM_SYSKEYDOWN) else "up  "
        print(f"[{args.label}]   t+{t - seen[0][0]:.3f}s {kind} "
              f"flags=0x{f:02X}" if seen else "")
    if len(downs) == 1:
        print(f"[{args.label}] => RESULT: bounces suppressed, real press kept")
    elif len(downs) == 3:
        print(f"[{args.label}] => RESULT: all 3 presses reached applications "
              f"(nothing filtering)")
    else:
        print(f"[{args.label}] => RESULT: unexpected ({len(downs)} downs)")

    if args.stop_blocker and blocker:
        subprocess.run(["taskkill", "/IM", os.path.basename(
            args.start_blocker), "/F"], capture_output=True)
        print(f"[{args.label}] blocker stopped", flush=True)


if __name__ == "__main__":
    main()
