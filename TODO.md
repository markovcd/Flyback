# To do

Work the user has asked for and nobody has started. Take an item off when it lands on `main`.

- **Compare the preset snapshots as pixels, as their comment already claims.** They compare PNG bytes today, so all 25 fail wherever .NET's zlib differs from the one that wrote the approved files. Diagnosis, proof and the fix to make are in [docs/handoff/preset-snapshots-compare-bytes.md](docs/handoff/preset-snapshots-compare-bytes.md). Make and check it on Windows.

- **Find why an undo freezes the window on a large patch.** Ctrl+Z on Mycelium stops the whole window for about two seconds on Windows; an edit does not. What is ruled out, what is left and how to find it are in [docs/handoff/undo-freezes-the-window.md](docs/handoff/undo-freezes-the-window.md). Find and fix it on Windows.

- **Account for every `Lazy<>` in the editor's container, and plan their removal.** List each `Lazy<T>` a constructor takes in `src/Flyback.App` and `src/Flyback.Viewer`, say which cycle it breaks and why the two classes need each other, and propose a refactor strategy (an event, a service split out of one side, or moving the call) that would let the container build both without one.
- **Loop any stretch of the seek bar.** Two handles on the bar set where a loop starts and ends, so a passage in the middle of a piece repeats rather than always the start.
- **Step a paused patch a frame at a time.** Left and right arrows over a paused full-screen picture, in the editor and the viewer, move the clock one frame back or on, for looking at what a picture does at one instant.
- **Count which features and modules are used.** The usage events (`src/Flyback.App/Statistics/Usage.cs`) say how many modules a played patch has, not which ones, and nothing about which editor features are reached for. Add both to what is sent to Aptabase, inside what ADR-0094 allows: shipped modules and features by name, a plugin's own under one name, nothing that could tie a report to a person or a patch.
- **`flyback-viewer --report`.** At the end of a run, print the frames a second it held, the slowest frame and what the sound cost, so a script or an agent can measure a patch without watching it.
- Allow to change a patch while agent is working. Also changing a patch shouldnt invalidate agent session
- Forbid opening files/presets during recording