#!/usr/bin/env python3
"""Wrap Chinese UI string literals in C# source with L.T(...) for bilingual UI.

Only *string literals containing CJK characters* are touched, which in this
codebase is exactly the set of user-facing strings (identifiers such as
"mouse_left", "key.", "config.txt", "e_key_filter=running" are ASCII and stay
untouched).  A proper tokenizer is used rather than a regex, so escaped quotes
and comments are handled correctly.

Usage:
    python wrap_i18n.py list   <file>...     # distinct CJK literals + counts
    python wrap_i18n.py wrap   <file>...     # rewrite the files in place
"""
import collections
import io
import re
import sys


def literals(src):
    """Yield (start, end, text) for every double-quoted literal containing CJK."""
    out = []
    i, n = 0, len(src)
    while i < n:
        c = src[i]
        if c == '"':
            j = i + 1
            buf = []
            while j < n:
                if src[j] == '\\':
                    buf.append(src[j:j + 2])
                    j += 2
                    continue
                if src[j] == '"':
                    break
                buf.append(src[j])
                j += 1
            text = "".join(buf)
            if any('\u4e00' <= ch <= '\u9fff' or '\u3000' <= ch <= '\u303f' or
                   '\uff00' <= ch <= '\uffef' for ch in text):
                out.append((i, j + 1, text))
            i = j + 1
            continue
        if c == '/' and i + 1 < n and src[i + 1] == '/':          # line comment
            i = src.find('\n', i)
            if i < 0:
                break
            continue
        if c == '/' and i + 1 < n and src[i + 1] == '*':          # block comment
            k = src.find('*/', i + 2)
            i = (k + 2) if k > 0 else n
            continue
        i += 1
    return out


def cmd_list(paths):
    counts = collections.Counter()
    for p in paths:
        src = io.open(p, encoding="utf-8").read()
        for _s, _e, t in literals(src):
            counts[t] += 1
    print(f"{len(counts)} distinct CJK literals\n")
    for t, c in counts.most_common():
        print(f"  [{c:>2}] {t!r}")


def cmd_wrap(paths):
    for p in paths:
        src = io.open(p, encoding="utf-8").read()
        lits = literals(src)
        # skip ones already wrapped: L.T( "..." ) / L.EN( "..." , ...)
        todo = []
        for s, e, t in lits:
            head = src[max(0, s - 12):s]
            if head.rstrip().endswith("L.T(") or head.rstrip().endswith("L.EN("):
                continue
            todo.append((s, e))
        out, last, wrapped = [], 0, 0
        for s, e in todo:
            out.append(src[last:s])
            out.append("L.T(" + src[s:e] + ")")
            last = e
            wrapped += 1
        out.append(src[last:])
        io.open(p, "w", encoding="utf-8", newline="").write("".join(out))
        print(f"{p}: wrapped {wrapped} of {len(lits)} literals "
              f"({len(lits) - len(todo)} already wrapped)")


def cmd_check(langfile, paths):
    """Verify every CJK literal in the sources has a key in the language file."""
    keys = set()
    for line in io.open(langfile, encoding="utf-8"):
        m = re.match(r'^\s*\{\s*"((?:[^"\\]|\\.)*)",', line.rstrip("\r\n"))
        if m:
            keys.add(m.group(1))
    total, missing = 0, []
    for p in paths:
        src = io.open(p, encoding="utf-8").read()
        for _s, _e, t in literals(src):
            total += 1
            if t not in keys and t not in missing:
                missing.append(t)
    print(f"{langfile}: {len(keys)} translation keys")
    print(f"sources: {total} CJK literal occurrences, {total - len(missing)} covered")
    if missing:
        print(f"\nMISSING {len(missing)}:")
        for t in missing:
            print(f"  {t!r}")
    else:
        print("full coverage")


if __name__ == "__main__":
    if len(sys.argv) < 3:
        print(__doc__)
        sys.exit(2)
    mode = sys.argv[1]
    if mode == "check":
        cmd_check(sys.argv[2], sys.argv[3:])
        sys.exit(0)
    files = sys.argv[2:]
    if mode == "list":
        cmd_list(files)
    elif mode == "wrap":
        cmd_wrap(files)
    else:
        print(__doc__)
        sys.exit(2)
