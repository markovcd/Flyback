# Running the app yourself

This covers launching the real Flyback window (full Avalonia, not a headless `UiTest`) to check something or to take a screenshot.

## Another Flyback may be open, and it is not yours

Before starting one, list what is running: `Get-Process Flyback` with its `Id`, `StartTime`, `MainWindowTitle` and `Path`. An instance you did not start belongs to the user or to another session, whichever checkout or build its path points at.

- **If one is running, wait for it to close.** Check again every minute or so. Do not click, type into, move, maximize or capture it in the meantime.
- **After more than 10 minutes of waiting, close it yourself** by its `Id`, then say so to the user: which instance it was (path, title, start time) and when you closed it.
- **Then open yours**, keep the `Id` that `Start-Process -PassThru` hands back, and act only on that process: its window handle for clicks, keys and captures, and its `Id` to close it when done. Never pick "the first Flyback window" or stop every `Flyback` process.

Before sending keys, bring your window to the front and check that `GetForegroundWindow()` is its handle, because `SendKeys` goes to whatever window is in front.

**Why:** a capture script once took the first Flyback window it found and began by stopping every `Flyback` process. The window it then drove, maximizing it, clicking its canvas and pressing Ctrl+F, was a Release build from the main checkout, not the Debug build the session had launched. It may have been the user's own.

A preset that makes sound plays through the user's speakers the moment it opens. To look at one, open a copy with the Output's `volume` at nought.

## Mark your window so it is plainly not to be touched

A window this session started looks exactly like one the user opened, and nothing on it says that a click lands in the middle of a script or that a keystroke goes somewhere unintended. Give yours a red title bar saying so, as soon as it has a handle and before maximizing, clicking or sending keys:

```powershell
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class W {
  [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int s);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool SetWindowTextW(IntPtr h, string t);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, System.Text.StringBuilder t, int n);
}
"@

while ($proc.MainWindowHandle -eq 0) { Start-Sleep -Milliseconds 400; $proc.Refresh() }
$h = $proc.MainWindowHandle

$sb = New-Object System.Text.StringBuilder 512
[void][W]::GetWindowTextW($h, $sb, 512)
$title = $sb.ToString()

$red = 0x001414C8; $white = 0x00FFFFFF
[void][W]::DwmSetWindowAttribute($h, 35, [ref]$red, 4)    # DWMWA_CAPTION_COLOR
[void][W]::DwmSetWindowAttribute($h, 36, [ref]$white, 4)  # DWMWA_TEXT_COLOR
[void][W]::DwmSetWindowAttribute($h, 34, [ref]$red, 4)    # DWMWA_BORDER_COLOR
[void][W]::SetWindowTextW($h, "DANGER - CLAUDE IS DRIVING - DO NOT CLICK")
```

The color is a `COLORREF`, `0x00BBGGRR`, and none of this touches the app: it is the window manager being told about a handle, so nothing has to be built or passed a flag for it. Windows 11 (build 22000) answers the three attributes; where it does not, the dangerous title is the whole of the marking. Flyback writes its own title back whenever the patch changes, so say it again after an edit or a preset change; the red survives it.

Put the caption back with `0xFFFFFFFF` (`DWMWA_COLOR_DEFAULT`) on all three attributes and `SetWindowTextW` with the title read above. Do that only where the red would end up in the work — a screenshot or a screen recording keeps the title bar in frame — and mark the window again as soon as the capture is taken, since it is still yours until you close it.
