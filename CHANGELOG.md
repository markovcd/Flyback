# Changelog

## 0.2.0 — 2026-09-10

76 commits since 0.1.0.

### Text language and live coding
- Added a text language that parses to a patch: lexer, parser, binder and step notation, specified in `docs/language.md` (ADR-0065).
- Added a printer that writes any patch back out as text, with line breaks laid out automatically.
- Added the `.fbks` source format, and patches can be saved as it. Bundles are now `.fbkb`.
- Added a code view built on AvaloniaEdit, with line numbers, error highlighting, current-line marking and syntax coloring.
- The opened file decides who owns the patch. With a `.fbks` the text is the document and Ctrl+Enter builds it onto a locked canvas. With a `.fbk`, bundle or preset the graph owns it and the text view shows a read-only printing.
- Added "Edit on the canvas" to hand ownership back from the text to the graph without saving a file.
- Modules keep their names and their state across a rebuild, so editing a playing patch no longer restarts every voice or empties every delay line.
- The caret selects a module in the inspector. Knob changes, sequencer tunes, scales, files and plugin fields are written back into the text where it already says them, and number boxes update the text as you type or scroll.
- Plugin fields are now part of the language, so printing a patch no longer loses them.
- Picking a preset while the text view is showing opens it as text, and loading a document no longer switches views.

### Undo
- One undo covers one action, whether that was a drag, a word typed or a document arriving, across both the text and the canvas.
- Ctrl+Z waits for a drag on the canvas to finish. Redo after an undo that crosses between text and canvas uses whichever stack still has the step.

### Assistant
- The assistant now builds patches by writing the text language rather than through graph calls.
- Added a Gemini adapter. Gemini models hear the patch's sound themselves instead of relying on a second model to describe it.
- Providers declare their own settings (text, list or switch), and the shell only draws them.
- Model lists come from probing the endpoint rather than a hardcoded list, which also replaces a default model that no longer accepted new keys.
- Added an optional setting that logs each conversation to its own file.
- Closing Settings any way other than Save restores the previous values.
- Tools are now defined as single entries of name, description, handler and availability.

### Performance
- Video rendering splits the program into per-frame, per-row and per-pixel stages, so work that can't vary between pixels runs once per frame or row. Renderer speedups range from 1.22x to 4.61x on the bundled presets, and audio is 3–8% faster.

### Modules
- Scope: the window is no longer capped at two seconds.
- Sequencer: the gate length now stops at half a step whether set by knob or wire.
- RGB starts out white instead of black.
- Knobs that count things now snap to whole numbers.
- Scan no longer asks for a wire into its clock socket.
- Fixed the descriptions of MIDI In's voice field and the Probe's LFO window.

### Interface
- The inspector gives a knob's reading its own wider column to the left of its number box, with bar widths matched across a module.
- The preview box hides when it could only show black, and gives its space to the inspector.
- The assistant footer no longer repeats a stale notice once a key is present.
- The computer keyboard stops acting as an instrument while the code editor has focus.
- Opening or saving a patch clears the preset list's selection.
- Added a glyph button in front of the preset picker, and moved the program group next to the patch controls.
- Added shared typography steps next to colors, and a single grey for secondary text.

### Presets
- Shipped presets no longer store module coordinates. They're placed by the automatic layout.

### Platform and internals
- Merged the sound and MIDI plugins into one plugin per platform: `WinIO`, `MacIO` and `LinuxIO`.
- Split the node editor into one class across a file per region.
- Unified the five routes by which a patch document arrives: its name, file kind and asset lookup now travel together.
- Renamed `PatchIo` to `PatchIO`, and added tests for the CLI patches, live values, choice fields and the image library.
