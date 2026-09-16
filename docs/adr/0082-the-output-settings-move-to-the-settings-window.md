# ADR-0082: The Output settings move to the settings window, and are kept

**Status:** Accepted · 2026-09-16 · *user-directed* · amends
[0037](0037-one-output-block-that-every-patch-has.md)'s rule that the shell
hangs its picture settings off the Output's panel; follows
[0034](0034-settings-in-a-file-the-key-in-the-operating-system.md) in where a
setting is kept

## Context

After [0080](0080-record-moves-to-the-toolbar-with-a-glyph-and-ctrl-r.md) and
[0081](0081-rewind-moves-to-the-toolbar-beside-record.md) took Record and Rewind
to the toolbar, the Output's panel held three rows under "Picture": the preview
size, the GPU switch and the processor switch. [0037](0037-one-output-block-that-every-patch-has.md)
put them there so they had somewhere to *be found*, and said in the same breath
that none of them is the patch — they are properties of the machine somebody is
working at.

A property of the machine that is forgotten at every launch is not being treated
as one. Somebody on a laptop whose GPU draws a stepped picture turned the switch
off every session, and nothing they could do made it stay off. The program
already had a place for machine-level choices that outlive a run: the settings
window, and the per-user data folder behind it.

## Decision

**The settings window has two sections: Agent settings and Output settings.**
The first is the assistant's section exactly as it was. The second holds Size,
Render and Processor — the same three controls, lent to the window the way the
assistant's are, so what they are set to is what the window shows.

**Amended: each section is a tab.** Stacked one above the other, the two made a
column that had to be scrolled past the assistant to reach the picture. A
`TabControl` was the simpler of the two ways to fix that — a list beside the
sections would have been a selection to keep in step with what is shown, which
a tab strip already is. The tabs are listed down the left, and the window is
one fixed size whichever is showing, so it does not jump as they are flicked
through; a section longer than that scrolls inside its own tab.

**One Save, at the bottom, for both.** It is one window and one errand. Closing
it any other way puts both sections back to what they held when it opened.

**Amended: nothing in the Output section acts until Save.** It first shipped
with the controls acting as they changed, so a size could be judged behind the
dimmed sheet before it was kept. That made Save only half of what it looked
like — the preview had already moved — and closing without saving had to undo
changes rather than simply drop them. Now the controls are a draft: Save hands
their values to the preview and the compiler and writes them out, and any other
way out just puts the controls back. The processor switch's label still follows
the switch as it is flicked, because it says what Save would do.

**They are kept in `output.json`, beside `assistant.json`** in the per-user data
folder, and read before the window is built. The size is kept as its width and
height rather than as a row of the list, so a list that gains or loses a row
still reads an old file; a size it no longer offers is the default. Like
`assistant.json`, the file is not load-bearing — missing or unreadable means the
defaults.

**The Output's panel carries its knobs and nothing else.** Selecting the Output
still shows no delete button, because it cannot be deleted.

## Consequences

**`MainWindow` takes the path to keep them at, and null keeps nothing.** That is
the opposite of `groupFolder`, where null is the usual place, and deliberately:
every UI test builds a window with no arguments, and a window that read the
machine's own file would make the defaults under test whatever the person
running the suite last saved. Only `FlybackApp` passes `OutputSettings.File`.

**The GPU switch is a request.** A machine that cannot use the shader still
draws on the processor whatever the file says, and the switch greys itself out
exactly as before; the saved answer is tried again next launch.

**The assistant panel's section lost its own Save button** to the window's, so
`SaveSettings` is internal and the tests that pressed the button call it.
