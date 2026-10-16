#!/usr/bin/env python3
"""Key chatter (switch bounce) monitor for Windows -- stdlib only.

Captures the raw HID keyboard stream via the Raw Input API (works no matter
which app has focus) and reports, per physical keyboard and per key, how often
a key goes down again only a few milliseconds after its previous press or
release.  That pattern -- two downs far too close together to be a human
double-tap -- is the signature of a worn switch bouncing ("chatter").

Usage:
    python keychatter_probe.py 20           # measure for 20 seconds
    python keychatter_probe.py 20 --csv out.csv
    python keychatter_probe.py --self-test  # verify the harness with synthetic input

While it runs: type normally, and hammer the suspect key (e.g. E) repeatedly
with deliberate, single presses.
"""
import ctypes
import ctypes.wintypes as wt
import sys
import time
import collections

user32 = ctypes.WinDLL("user32", use_last_error=True)
kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)

WM_INPUT = 0x00FF
WM_DESTROY = 0x0002
RIDEV_INPUTSINK = 0x00000100
RID_INPUT = 0x10000003
RIDI_DEVICENAME = 0x20000007
RIM_TYPEKEYBOARD = 1

KEY_NAMES = {
    0x45: "E", 0x48: "H", 0x4F: "O", 0x54: "T", 0x41: "A", 0x53: "S",
    0x44: "D", 0x57: "W", 0x51: "Q", 0x52: "R", 0x20: "SPACE",
    0x0D: "ENTER", 0x08: "BACKSPACE", 0x1B: "ESC", 0x09: "TAB",
}


# 64-bit handles must be declared, otherwise ctypes truncates them to 32 bits.
user32.RegisterClassW.argtypes = [ctypes.c_void_p]
user32.RegisterClassW.restype = wt.ATOM
user32.UnregisterClassW.argtypes = [wt.LPCWSTR, wt.HINSTANCE]
user32.CreateWindowExW.argtypes = [wt.DWORD, wt.LPCWSTR, wt.LPCWSTR, wt.DWORD,
                                   ctypes.c_int, ctypes.c_int, ctypes.c_int,
                                   ctypes.c_int, wt.HWND, wt.HMENU,
                                   wt.HINSTANCE, ctypes.c_void_p]
user32.CreateWindowExW.restype = wt.HWND
user32.DestroyWindow.argtypes = [wt.HWND]
user32.DefWindowProcW.restype = ctypes.c_long
user32.DefWindowProcW.argtypes = [wt.HWND, wt.UINT, wt.WPARAM, wt.LPARAM]
user32.DispatchMessageW.argtypes = [ctypes.POINTER(wt.MSG)]
user32.PeekMessageW.argtypes = [ctypes.POINTER(wt.MSG), wt.HWND, wt.UINT,
                                wt.UINT, wt.UINT]
user32.RegisterRawInputDevices.argtypes = [ctypes.c_void_p, wt.UINT, wt.UINT]
user32.GetRawInputData.restype = wt.UINT
user32.GetRawInputData.argtypes = [wt.HANDLE, wt.UINT, ctypes.c_void_p,
                                   ctypes.POINTER(wt.UINT), wt.UINT]
user32.GetRawInputDeviceInfoW.restype = wt.UINT
user32.GetRawInputDeviceInfoW.argtypes = [wt.HANDLE, wt.UINT, ctypes.c_void_p,
                                          ctypes.POINTER(wt.UINT)]
user32.SendInput.argtypes = [wt.UINT, ctypes.c_void_p, ctypes.c_int]
user32.SendInput.restype = wt.UINT
kernel32.GetModuleHandleW.restype = wt.HMODULE


class RAWINPUTDEVICE(ctypes.Structure):
    _fields_ = [("usUsagePage", wt.USHORT), ("usUsage", wt.USHORT),
                ("dwFlags", wt.DWORD), ("hwndTarget", wt.HWND)]


class RAWINPUTHEADER(ctypes.Structure):
    _fields_ = [("dwType", wt.DWORD), ("dwSize", wt.DWORD),
                ("hDevice", wt.HANDLE), ("wParam", wt.WPARAM)]


class RAWKEYBOARD(ctypes.Structure):
    _fields_ = [("MakeCode", wt.USHORT), ("Flags", wt.USHORT),
                ("Reserved", wt.USHORT), ("VKey", wt.USHORT),
                ("Message", wt.UINT), ("ExtraInformation", wt.ULONG)]


class RAWINPUT(ctypes.Structure):
    _fields_ = [("header", RAWINPUTHEADER), ("keyboard", RAWKEYBOARD)]


# ---- minimal SendInput plumbing, only used by --self-test ----
class MOUSEINPUT(ctypes.Structure):
    _fields_ = [("dx", wt.LONG), ("dy", wt.LONG), ("mouseData", wt.DWORD),
                ("dwFlags", wt.DWORD), ("time", wt.DWORD),
                ("dwExtraInfo", ctypes.POINTER(wt.ULONG))]


class KEYBDINPUT(ctypes.Structure):
    _fields_ = [("wVk", wt.WORD), ("wScan", wt.WORD), ("dwFlags", wt.DWORD),
                ("time", wt.DWORD), ("dwExtraInfo", ctypes.POINTER(wt.ULONG))]


class _INPUTUNION(ctypes.Union):
    _fields_ = [("ki", KEYBDINPUT), ("mi", MOUSEINPUT)]


class INPUT(ctypes.Structure):
    _fields_ = [("type", wt.DWORD), ("u", _INPUTUNION)]


INPUT_KEYBOARD = 1
KEYEVENTF_KEYUP = 0x0002
SIZEOF_INPUT = ctypes.sizeof(INPUT)


def send_vk(vk, up=False):
    inp = INPUT(type=INPUT_KEYBOARD)
    inp.u.ki = KEYBDINPUT(wVk=vk, wScan=0,
                          dwFlags=KEYEVENTF_KEYUP if up else 0,
                          time=0, dwExtraInfo=None)
    if user32.SendInput(1, ctypes.byref(inp), SIZEOF_INPUT) != 1:
        raise ctypes.WinError(ctypes.get_last_error())


events = []            # (perf_counter, vkey, is_down, device_handle)
device_names = {}


def device_name(handle):
    if handle in device_names:
        return device_names[handle]
    size = wt.UINT(0)
    ok = user32.GetRawInputDeviceInfoW(handle, RIDI_DEVICENAME, None,
                                       ctypes.byref(size))
    name = "<injected/unknown>"
    if ok or size.value:
        buf = ctypes.create_unicode_buffer(size.value + 2)
        if user32.GetRawInputDeviceInfoW(handle, RIDI_DEVICENAME, buf,
                                         ctypes.byref(size)) > 0:
            name = buf.value
    device_names[handle] = name
    return name


WNDPROC = ctypes.WINFUNCTYPE(ctypes.c_long, wt.HWND, wt.UINT, wt.WPARAM,
                             wt.LPARAM)


class WNDCLASSW(ctypes.Structure):
    """ctypes.wintypes does not ship WNDCLASSW; declaring it here so the
    handle-sized fields keep their 64-bit width."""
    _fields_ = [("style", wt.UINT), ("lpfnWndProc", WNDPROC),
                ("cbClsExtra", ctypes.c_int), ("cbWndExtra", ctypes.c_int),
                ("hInstance", wt.HINSTANCE), ("hIcon", wt.HICON),
                ("hCursor", wt.HANDLE), ("hbrBackground", wt.HBRUSH),
                ("lpszMenuName", wt.LPCWSTR), ("lpszClassName", wt.LPCWSTR)]


RIM_INPUT = 0        # this process is in the foreground
RIM_INPUTSINK = 1    # background capture, i.e. what RIDEV_INPUTSINK gives us


def wnd_proc(hwnd, msg, wparam, lparam):
    if msg == WM_INPUT:
        # wParam is RIM_INPUT in the foreground and RIM_INPUTSINK in the
        # background; both are real input and must be handled.
        if wparam in (RIM_INPUT, RIM_INPUTSINK):
            size = wt.UINT(0)
            user32.GetRawInputData(lparam, RID_INPUT, None, ctypes.byref(size),
                                   ctypes.sizeof(RAWINPUTHEADER))
            if size.value:
                buf = ctypes.create_string_buffer(size.value)
                if user32.GetRawInputData(lparam, RID_INPUT, buf,
                                          ctypes.byref(size),
                                          ctypes.sizeof(RAWINPUTHEADER)) > 0:
                    ri = ctypes.cast(buf, ctypes.POINTER(RAWINPUT)).contents
                    if ri.header.dwType == RIM_TYPEKEYBOARD:
                        kb = ri.keyboard
                        flags = kb.Flags
                        # bit 0 = break (key up); bit 1 = E0; bit 2 = E1
                        is_down = not (flags & 0x01)
                        if not (flags & 0x04) and kb.VKey:
                            events.append((time.perf_counter(), kb.VKey,
                                           is_down, ri.header.hDevice))
        return 0
    if msg == WM_DESTROY:
        return 0
    return user32.DefWindowProcW(hwnd, msg, wparam, lparam)


def pump(seconds, hwnd):
    deadline = time.perf_counter() + seconds
    msg = wt.MSG()
    while time.perf_counter() < deadline:
        while user32.PeekMessageW(ctypes.byref(msg), hwnd, 0, 0, 1):
            user32.TranslateMessage(ctypes.byref(msg))
            user32.DispatchMessageW(ctypes.byref(msg))
        time.sleep(0.002)


def live(seconds, csv_path=None):
    hinst = kernel32.GetModuleHandleW(None)
    wc = WNDCLASSW()
    wc.lpfnWndProc = WNDPROC(wnd_proc)
    wc.hInstance = hinst
    wc.lpszClassName = "KeyDebounceProbe"
    if not user32.RegisterClassW(ctypes.byref(wc)):
        raise ctypes.WinError(ctypes.get_last_error())
    hwnd = user32.CreateWindowExW(0, wc.lpszClassName, "probe", 0, 0, 0, 0, 0,
                                  None, None, hinst, None)
    if not hwnd:
        raise ctypes.WinError(ctypes.get_last_error())

    rid = RAWINPUTDEVICE(usUsagePage=0x01, usUsage=0x06,
                         dwFlags=RIDEV_INPUTSINK, hwndTarget=hwnd)
    if not user32.RegisterRawInputDevices(ctypes.byref(rid), 1,
                                          ctypes.sizeof(RAWINPUTDEVICE)):
        raise ctypes.WinError(ctypes.get_last_error())

    print(f"Capturing raw keyboard input for {seconds}s -- type now, "
          f"especially the suspect key.  (Ctrl+C to stop early)")
    try:
        pump(seconds, hwnd)
    except KeyboardInterrupt:
        pass

    user32.DestroyWindow(hwnd)
    user32.UnregisterClassW(wc.lpszClassName, hinst)
    report(events, csv_path)


def report(evts, csv_path=None):
    if not evts:
        print("\nNo raw keyboard events captured at all -- is a keyboard "
              "connected, and did anything get typed?")
        return
    downs = [(t, vk, dev) for t, vk, d, dev in evts if d]
    print(f"\nCaptured {len(evts)} events, {len(downs)} key-downs.")
    print("Devices seen:")
    for dev in sorted({e[3] for e in evts}, key=str):
        print("  " + device_name(dev))

    perkey = collections.defaultdict(list)
    for t, vk, dev in downs:
        perkey[(dev, vk)].append(t)

    header = (f"{'key':<6} {'device':<14} {'presses':>7} "
              f"{'<40ms gaps':>10} {'<15ms':>6} {'min gap ms':>10}")
    print("\n" + header)
    print("-" * len(header))
    worst = []
    for (dev, vk), ts in sorted(perkey.items(), key=lambda kv: kv[0][1]):
        gaps = [b - a for a, b in zip(ts, ts[1:])]
        short = [g for g in gaps if g < 0.040]
        tiny = [g for g in gaps if g < 0.015]
        tag = KEY_NAMES.get(vk, f"vk{vk:02X}")
        devshort = device_name(dev).split("#")[-1][:13]
        print(f"{tag:<6} {devshort:<14} {len(ts):>7} {len(short):>10} "
              f"{len(tiny):>6} {min(gaps) * 1000 if gaps else 0:>10.1f}")
        if tiny:
            worst.append((tag, len(tiny), min(gaps) * 1000))

    if worst:
        print("\nSwitch-bounce signature (two key-downs <15ms apart) found on:")
        for tag, n, mn in sorted(worst, key=lambda x: -x[1]):
            print(f"  {tag}: {n} occurrences, shortest {mn:.1f}ms "
                  f"-> chatter")
    else:
        print("\nNo <15ms double key-downs seen: either no chatter on the "
              "keys you pressed, or you only pressed the clean keys.")

    if csv_path:
        with open(csv_path, "w", encoding="utf-8") as fh:
            fh.write("t_offset,is_down,vkey,device\n")
            t0 = evts[0][0]
            for t, vk, d, dev in evts:
                fh.write(f"{t - t0:.6f},{int(d)},{vk},{device_name(dev)}\n")
        print(f"\nRaw event log written to {csv_path}")


def self_test():
    """Verify the capture path with programmatically injected keystrokes."""
    import threading

    def inject():
        time.sleep(0.5)
        # three clean, humanly spaced taps of E
        for _ in range(3):
            send_vk(0x45)
            time.sleep(0.03)
            send_vk(0x45, up=True)
            time.sleep(0.12)
        # one press that "bounces": down, up after 3ms, down again after 5ms
        send_vk(0x45)
        time.sleep(0.003)
        send_vk(0x45, up=True)
        time.sleep(0.005)
        send_vk(0x45)
        time.sleep(0.03)
        send_vk(0x45, up=True)

    threading.Thread(target=inject, daemon=True).start()
    live(3)


if __name__ == "__main__":
    args = [a for a in sys.argv[1:]]
    csvp = None
    if "--csv" in args:
        i = args.index("--csv")
        csvp = args[i + 1]
        del args[i:i + 2]
    if "--self-test" in args:
        self_test()
    else:
        secs = float(args[0]) if args else 20.0
        live(secs, csvp)
