# The web viewer renders a patch's sound slower than the web editor's worker

Found on 2026-09-30, on `main` at `7d651db1`. It is on TODO.md; take it off there, and
delete this file, in the commit that lands it.

- **Severity:** Medium
- **Status:** Open

## What is wrong

The same worker, `speaker.js`, plays the sound for the web viewer and the web editor,
yet on one machine Acid at 2× rendered at about 1.1–1.2× real time under the viewer
and about 4× under the editor. The viewer's chunks came late about 10% of the time
and, after a burst of 13 late chunks in half a second, the late-chunk rule stepped it
down to 1×, twice in two runs. The editor never stepped down.

## Evidence

Measured on the ahead-of-time compiled site (`AOT=true ./scripts/pages.sh`), in Chrome
with the browser pane hidden and the sound muted, by reading `flyback.status()` in the
viewer and `flyback.sound()` in the editor. Not yet repeated with the pane visible or
on a second machine; a hidden pane may have cost the viewer's page priority, but the
editor was measured the same way.

## Impact

A machine that is barely keeping up sees the viewer step down to 1× where the editor
holds 2×, for the same patch. Whatever the viewer does differently costs it most of the
headroom the 2× default gained (ADR-0168).

## Suggested fix

Find the difference first; the two pages drive one worker, so it is in what each asks
of it. Candidates: the viewer opens the picture's program in the worker too
(`WebSound.Listen` refills charts and packs state every 15 ms, `STATE_EVERY` in
`speaker.js`), where the editor's worker only measures the Meters it is asked for; the
viewer's `Listen` may be paying for that on the sound's thread. Measure with the
picture off (`?picture=off`) and with a Scope-free patch to split it. Then the step
down should hold at 2× where the editor does.
