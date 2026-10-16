#!/usr/bin/env python3
"""Analyse a keychatter_probe CSV capture.

Accepts both CSV layouts (the probe used to emit a duplicated vkey column):
    rel_time_s, vkey, is_down, device
    rel_time_s, vkey, is_down, vkey, device
(the first column is always the time relative to the first captured event).

Prints, per key: down-to-down gap histogram, the shortest gaps, and -- for the
suspect key -- the down/up micro-trace of the shortest burst so bounce trains
can be told apart from legitimate repeats.

Usage:  python analyze_capture.py capture.csv [SUSPECT_KEY_NAME]
"""
import collections
import csv
import sys

KEYS = {0x45: "E", 0x48: "H", 0x4F: "O", 0x54: "T", 0x41: "A", 0x53: "S",
        0x44: "D", 0x57: "W", 0x51: "Q", 0x52: "R", 0x20: "SPACE",
        0x0D: "ENTER", 0x08: "BACKSPACE", 0x09: "TAB", 0x1B: "ESC",
        0x5B: "LWin", 0x10: "SHIFT", 0x11: "CTRL", 0x12: "ALT"}


def load(path):
    """Parse a capture CSV, driven by its header.

    Two layouts exist in the wild: the probe's current
    ``t_offset,is_down,vkey,device`` and the older / hand-edited
    ``<time>,vkey,is_down,vkey,device`` where the vkey column was duplicated.
    Never assume column positions -- read the header.
    """
    ev = []
    with open(path, newline="", encoding="utf-8") as fh:
        for row in csv.reader(fh):
            if len(row) < 4 or not row[0] or not row[0][0].isdigit():
                continue
            t = float(row[0])
            if row[1] in ("0", "1"):            # is_down comes second
                down, vk = row[1] == "1", int(row[2])
            else:                                # vkey comes second
                vk, down = int(row[1]), row[2] == "1"
            ev.append((t, vk, down, row[-1]))
    return ev


def buckets(gaps):
    edges = [0, 3, 5, 8, 12, 20, 30, 50, 80, 120, 200, 400, 10 ** 9]
    out = collections.OrderedDict()
    for lo, hi in zip(edges, edges[1:]):
        n = sum(1 for g in gaps if lo <= g < hi)
        if n:
            out[f"{lo}-{hi}" if hi < 10 ** 9 else f">{lo}"] = n
    return out


def main():
    path = sys.argv[1]
    suspect = (sys.argv[2] if len(sys.argv) > 2 else "E").upper()
    ev = load(path)
    if not ev:
        print("no events"); return
    print(f"{path}: {len(ev)} events, {sum(1 for e in ev if e[2])} key-downs, "
          f"{ev[-1][0] - ev[0][0]:.1f}s span")
    print("devices:", sorted({e[3].split('#')[1] if '#' in e[3] else e[3]
                              for e in ev}))
    per = collections.defaultdict(list)
    for t, vk, down, dev in ev:
        if down:
            per[(dev, vk)].append(t)
    for (dev, vk), ts in sorted(per.items(), key=lambda kv: kv[0][1]):
        gaps = [(b - a) * 1000 for a, b in zip(ts, ts[1:])]
        name = KEYS.get(vk, f"vk{vk:02X}")
        print(f"  {name:<8} downs={len(ts):>4} "
              f"min_gap={min(gaps) if gaps else 0:>7.1f}ms "
              f"gaps<20ms={sum(1 for g in gaps if g < 20):>4} "
              f"20-50ms={sum(1 for g in gaps if 20 <= g < 50):>3} "
              f"50-120ms={sum(1 for g in gaps if 50 <= g < 120):>3} "
              f">=120ms={sum(1 for g in gaps if g >= 120):>4}")

    for (dev, vk), ts in sorted(per.items(), key=lambda kv: kv[0][1]):
        if KEYS.get(vk, "").upper() != suspect:
            continue
        gaps = [(b - a) * 1000 for a, b in zip(ts, ts[1:])]
        print(f"\n  {suspect}: down-to-down gap histogram (ms): {buckets(gaps)}")
        short = sorted(gaps)[:12]
        print(f"  {suspect}: 12 shortest down-to-down gaps (ms): "
              f"{[round(g, 1) for g in short]}")
        # micro-trace of the first bounce train
        first = min(ts)
        trace = [(round((t - first) * 1000, 2), "down" if d else "up")
                 for t, k, d, dv in ev
                 if k == vk and dv == dev and first <= t <= first + 0.4]
        print(f"  {suspect}: micro-trace of one press (ms since press):")
        print("   ", trace)


if __name__ == "__main__":
    main()
