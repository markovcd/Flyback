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
way out just puts the controls back. The CPU code switch's label still follows
the switch as it is flicked, because it says what Save would do.

**They are kept in `output.json`, beside `assistant.json`** in the per-user data
folder, and read before the window is built. The size is kept as its width and
height rather than as a row of the list, so a list that gains or loses a row
still reads an old file; a size it no longer offers is the default. Like
`assistant.json`, the file is not load-bearing — missing or unreadable means the
defaults.

**The Output's panel carries its knobs and nothing else.** Selecting the Output
still shows no delete button, because it cannot be deleted.

**Amended: the section is called Graphics settings.** "Output" named the module
the rows came from, which is where they no longer are, and read as though it
covered the recording and sound sections beside it too. The class and its file
keep the name `OutputSettings` and `output.json`, since that file holds all
three sections and renaming it would lose what somebody had already saved.

**Amended: the two switches say what they switch between.** All four
combinations of Render and the processor switch are meaningful, but two of them
did not look it. With the GPU drawing, the processor switch reaches only the
sound, so flipping it changed nothing on screen; and the render switch said
"GPU" either way, so off named nothing at all. Render now reads GPU or CPU as
the processor switch already read Compiled or Interpreted. That switch is
called *CPU code* rather than *Processor* — beside a renderer that can be the
CPU, "Processor" read as the same setting twice — and a line under it says it
runs the sound, and the picture only while the CPU draws it. It is not greyed
out while the GPU draws, because it still decides how the sound runs.

**Amended: two more sections, and a fourth setting on the agent's.** Four
values were fixed in code that are choices about this machine rather than
about any patch, and the window was already the place for those:

- *Recording settings* — a take's frame rate, picked from 24, 25, 30, 50 and
  60, and its JPEG quality from 1 to 100. Both are read when a take starts, so
  a take already running keeps what it began with. The command line's own
  defaults are unchanged; a render is a deterministic export and takes its
  numbers from its arguments.
- *Sound settings* — the latency the device is asked for, from 10 to 200 ms.
  The device is opened once a launch and the engine is built around it, so a
  saved latency is heard from the next launch, and the tab says so. Reopening
  the device in place would mean rebuilding the engine under a running audio
  thread and the preview clock that follows it, for a setting somebody changes
  once.
- *Turns per conversation*, on the agent's tab, kept in `assistant.json` with
  the rest of that section. Unlike the latency it reaches the conversation
  already going: a limit raised because a conversation ran out is raised for
  that conversation.

The recording and sound values go in `output.json` beside the picture's. A value
edited by hand out of range is brought into it on reading rather than trusted —
a frame rate of nought would divide by it, and a latency of a minute would stall
the sound. A saved value one of the lists does not offer shows as the nearest
one that it does.

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
