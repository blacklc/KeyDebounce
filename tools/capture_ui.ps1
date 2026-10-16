# capture_ui.ps1 -- screenshot a KeyDebounce window via PrintWindow (z-order
# independent, so it works even when the window is behind others).
#   powershell -File capture_ui.ps1 -Out <png> [-ProcName KeyDebounce]
param(
    [Parameter(Mandatory=$true)][string]$Out,
    [string]$ProcName = "KeyDebounce"
)
Add-Type -Namespace W -Name Cap -MemberDefinition '
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);'
Add-Type -AssemblyName System.Drawing

$p = Get-Process $ProcName -ErrorAction SilentlyContinue |
     Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $p) { Write-Output "NO WINDOW ($ProcName)"; exit 1 }

$r = New-Object W.Cap+RECT
[void][W.Cap]::GetWindowRect($p.MainWindowHandle, [ref]$r)
$w = $r.R - $r.L; $h = $r.B - $r.T
$bm = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bm)
$hdc = $g.GetHdc()
$ok = [W.Cap]::PrintWindow($p.MainWindowHandle, $hdc, 0)
$g.ReleaseHdc($hdc)
$bm.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bm.Dispose()
Write-Output ("captured pid " + $p.Id + " " + $w + "x" + $h + " -> " + $Out + " (PrintWindow=" + $ok + ")")
