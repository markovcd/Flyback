# Running the app yourself

This covers launching the real Flyback window (full Avalonia, not a headless `UiTest`) to check something or to take a screenshot.

## Another Flyback may be open, and it is not yours

Before starting one, list what is running: `Get-Process Flyback` with its `Id`, `StartTime`, `MainWindowTitle` and `Path`. An instance you did not start belongs to the user or to another session, whichever checkout or build its path points at.

- **If one is running, wait for it to close.** Check again every minute or so. Do not click, type into, move, maximize or capture it in the meantime.
- **After more than 10 minutes of waiting, close it yourself** by its `Id`, then say so to the user: which instance it was (path, title, start time) and when you closed it.
- **Then open yours**, keep the `Id` that `Start-Process -PassThru` hands back, and act only on that process: its window handle for clicks, keys and captures, and its `Id` to close it when done. Never pick "the first Flyback window" or stop every `Flyback` process.

Before sending keys, bring your window to the front and check that `GetForegroundWindow()` is its handle, because `SendKeys` goes to whatever window is in front.

**Why:** a capture script once took the first Flyback window it found and began by stopping every `Flyback` process. The window it then drove, maximizing it, clicking its canvas and pressing Ctrl+F, was a Release build from the main checkout, not the Debug build the session had launched. It may have been the user's own.

A preset that makes sound plays through the user's speakers the moment it opens. To look at one, open a copy with the Output's `volume` at nought, or leave it to the user (see the `site-screenshots` skill).
