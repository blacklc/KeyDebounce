# Third-party notices

KeyDebounce is a launcher and UI around an existing open-source engine. Nothing in
this repository is derived from closed-source software, and no third-party code is
bundled inside `KeyDebounce.exe`.

---

## 1. KeyboardChatterBlocker (the filtering engine)

- **Project:** https://github.com/FreneticLLC/KeyboardChatterBlocker
- **Version used:** 1.21 (released 2026-08-02)
- **Author:** Alex "mcmonkey" Goodwin; later maintained by Frenetic LLC
- **License:** MIT
- **Relationship:** separate program, used as-is. KeyDebounce starts, stops and
  configures it; it is not linked into or modified by KeyDebounce.

**Distributed binary** (in the release archive, *not* committed to this repository):

| | |
|---|---|
| File | `KeyboardChatterBlocker.exe` |
| SHA-256 | `da47b9b418403245b5854328bdfea8fb25e38e831cc1d9f25145da39c1f6d2b0` |
| Authenticode | **Valid** — signer `CN=Frenetic LLC, O=Frenetic LLC, L=Torrance, S=California, C=US` |
| Source of the copy | the official GitHub release asset for tag `1.21` |

Verify your copy yourself before running it:

```powershell
sha256sum KeyboardChatterBlocker.exe     # or: Get-FileHash KeyboardChatterBlocker.exe
powershell -c "(Get-AuthenticodeSignature .\KeyboardChatterBlocker.exe).Status"   # expect: Valid
powershell -c "(Get-AuthenticodeSignature .\KeyboardChatterBlocker.exe).SignerCertificate.Subject"
```

### Its license, reproduced as required

```
MIT License

Copyright (c) 2019-2024 Alex "mcmonkey" Goodwin
Copyright (c) 2024-2026 Frenetic LLC

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## 2. .NET Framework / WinForms

KeyDebounce uses only the .NET Framework class library shipped with Windows
(`System.Windows.Forms`, `System.Drawing`, `System`, `mscorlib`) through the
in-box compiler `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`.
No NuGet packages, no third-party managed libraries, no vendored code.

## 3. Development tooling (not part of the shipped program)

The scripts in `tools/` are written for this project and use only the Python standard
library (or PowerShell built-ins). They are development aids: the shipped
`KeyDebounce.exe` never executes them and does not depend on Python.

## 4. Key names and configuration format

The `key.<Name>` configuration vocabulary follows the upstream engine's own parser
(`KeysHelper.TryGetKey` → the .NET `Keys` enum), plus the six mouse pseudo-names
`mouse_left`, `mouse_right`, `mouse_middle`, `mouse_forward`, `mouse_backward` and
`wheel_change`. KeyDebounce only writes names that vocabulary accepts.
