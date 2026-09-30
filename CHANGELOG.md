# Changelog

## Unreleased

- A shared preset opened once is kept with its still, its stars and everything the preset site said of it, so the gallery lists it and it opens while the site does not answer; one the site answers it has taken down is forgotten.
- `flyback-cli shot` draws the editor's window with a patch open into a PNG, its picture at the second `--at` names, with no screen and no sound; `--crop` keeps only the canvas around the modules.
- Flyback Theme is a showcase preset: Flyback's own song, three minutes of synthwave in A minor on a two-channel scope.
- The Output's Volume is on the toolbar, beside the seek bar.
- Emptying the length box beside the seek bar takes the patch's length away.
- The viewer and the web viewer show no seek bar for a patch that does not say its length, and play it on past three minutes.
- The editor starts again with a library folder set under Settings → Files.
- `flyback-cli plugin describe` says what a `.fbkp` package is and what its code reaches, as the install dialog would, without running it.
- The preset gallery shows the shipped presets by stills the release drew, rather than drawing each one; `flyback-cli stills` draws them.
- The presets page lists the presets Flyback ships with, marked as built in, beside the shared ones, and on GitHub Pages alone; a search and the tags narrow both; Play opens one in the web viewer, which leaves picking a preset to the page, and Edit in the web editor.
- Overworld's verse is on Em Am G Bm, rather than the chords Flyback Theme opens with.
- Whole band, Acid, Mycelium, Bronze, Outrun, Phase, Fracture, Overworld and Vigil last one pass of their arrangement, so they loop where they come round, and Phase goes through its canons again the second time.
- `flyback-cli info` says how long a patch plays, and whether it sets its length.
- `flyback-cli pack` and `check` take `--preset`, and a preset name nobody shipped is answered with the ones there are.
- The editor works on a touch screen: two fingers pan and zoom, a finger held still is a right-click, and the toolbar and panel gain buttons for adding a module, framing the patch and duplicating.
- A window too narrow for the canvas beside the preview and the inspector, a tablet or a phone held upright, shows one at a time by the side button, and its toolbar wraps onto more rows.
- The module list and the preset gallery a finger opens wait for their box to be tapped before bringing up the on-screen keyboard.
- Time says how long the patch plays for and how far through it the patch is, as `length` and `progress`; the text reads them as `t.length` and `t.progress`.
- The toolbar's side button puts the preview and the inspector away, giving the canvas their width.
- The status bar and the stats line name what draws the picture (OpenGL, Direct3D, WebGL or CPU), Settings → Graphics picks it in one Renderer box, and the op count reads as picture/sound.

### Fixes
- A patch file with a null where a module or a wire belongs is refused on opening; one saved with a byte order mark opens on the preset site and in the web viewer and editor.
- A name, author or description cut to length no longer splits an emoji in two.
- The preset site starts when somebody has already submitted one of its default plugins, its search ignores case beyond ASCII, and a file it could send compressed says it varies by encoding either way.
- Printed as text, a patch keeps its very small numbers, fractional counts, very low notes and quoted descriptions exactly, and names a plugin's module in full where a built-in shares its short name.
- A knob, `off` or back-wire statement inside a def's braces is read as one.
- Turning a knob on the canvas no longer rewrites a number the text shares with other knobs.
- The assistant cannot remove the Output, set a knob past what a float holds, or take a handle the text cannot print.
- A key pasted with a trailing space is still kept out of messages, and one pasted into a message stays out of the conversation log.
- The assistant reads an endpoint's refusal however it is shaped and a token count written with a fraction, and a saved conversation with a field written twice opens as none.

## 0.5.1 — 2026-09-29

10 commits since 0.5.0.

- The editor finds shared presets and plugins, and sends letters, on the preset site at flyback.nasik2137.uk.
- The web viewer plays on the computer keyboard, with a seek bar, a volume slider, a slider per panel knob, a line saying what the patch is for, resolutions up to 720p, full screen and the editor's preset groups; a preset site preset plays there alone.
- The preset site refuses a bundle that unpacks past 128 MB, keeps its admin cookie to HTTPS, counts an IPv6 visitor by its /64, and `render-presets` reads only the files a preset carries.

## 0.5.0 — 2026-09-29

528 commits since 0.4.0.

### Sharing and plugins
- Presets and plugins people make are shared on a preset site linked from the website, with a picture, a sound, star ratings and a report button; the preset gallery and the plugins window list what it offers.
- A plugins window beside the settings button searches the installed plugins and the shared ones together, says what failed to load, and installs, updates or removes a plugin; opening a `.fbkp` package shows the plugin, its modules and what it reaches, and installs it.
- The shipped plugins have a name, author, description, tags and a preview picture.
- A patch that will not open for want of a plugin offers the one the site has, and opens again once it is installed.
- A plugin folder loads only once you allow it, and a patch or bundle from somebody else reaches only its own files.
- A plugin's preset can carry the sound files and pictures it plays.

### Modules
- The preset site starts with Easy, a plugin of two modules that sound good with nothing wired and never clip: Easy Synth, a whole synth, and Easy Drum, nine drum sounds that each play their own rhythm; Warehouse, three minutes of acid house, is played on both.
- The preset site starts with Figures, three modules that are each a picture and a sound: Plate, Harmonograph and Overtones; Vigil, dark ambient in D phrygian dominant, is played on all three.
- The preset site starts with Fractals, three modules about one point c: Mandelbrot, Julia and Orbit, its sound, with Dive and Julia walk as presets.
- Added Arrangement: up to eight parts get a level in each of up to thirty-two sections of a piece, gliding where asked.
- Added Chord, one of 23 chords on a root, and Auto Chord, the seventh chord on a played note or a step of one of 26 scales; the computer keyboard's Scale layout plays a tonic and one of those scales.
- Added Chance: each note on a gate plays on `gate` as often as a knob says and on `else` the rest of the time.
- Added Arc, part of a ring filled like a dial by its `sweep`.
- Added Crush, a bitcrusher.
- Added Auto remap, a Remap whose ranges are read off its wires; a wire between two different ranges has a mark that puts one in, and a wire swinging past its socket's range is drawn in orange.
- A Sample plays an MP3 as well as a WAV.
- Frequency and delay-time knobs sweep in decades, an oscillator's `freq` reaches from a slow wobble up to 20 kHz, and a panel knob can sweep logarithmically from its menu.
- The Frequency module is gone, and Random is now Noise and the old Noise is Clouds; a patch holding any of the old ones no longer opens.

### MIDI
- Added Clock In, which keeps a patch's beats, tempo and transport to an instrument's MIDI clock.
- The module list adds a plugged-in instrument Flyback knows by name, its clock and a MIDI In per track; the Elektron Syntakt ships known.
- A MIDI In can listen to one channel of its instrument, and a note played over a busy voice gives the voice back when let go.
- A knob's menu learns it and every knob after it in one pass, keeping an instrument's channel.

### Presets
- Dub is now No Sense Dub, roots dub in A minor: a one drop at 74 BPM, a drop to Patois and silence, steppers at 148, a drawbar organ and six panel knobs.
- Tranquility, on the preset site: psytrance in G sharp with Apollo 11 on the radio and alien FM chatter on three panel knobs.
- Irrational: seven orbits, each on a knob, drawing together into an alignment never quite exact.
- Beat you can see and Two echoes join the teaching presets, Mycelium speaks, Slow weather has six panel knobs and thirty minutes, and Plasma is three drifting fields in blue, cream and amber.

### Text patches
- A patch's groups, knob panel, needed plugins, author, tags and description are written in its text and read back.
- A patch written out as text follows its own chain and keeps its arithmetic as arithmetic.
- A pipe lands on `in`, a module's only socket, a position, a module's one color socket, or the socket written `socket: _`, and a def takes its arguments by name, with defaults and a pipe on its `in`.
- Patch text pastes onto the canvas as its modules and groups, and modules copied off a canvas paste into the text view as text.
- A mistake in a text patch is said as what it is and where.

### Command line
- `flyback-cli render` draws the picture on the graphics card with no window, and takes what it is not given from the editor's settings.
- `flyback-cli render`, `print` and `info` take a shipped preset with `--preset`, and `--presets` lists them.
- `flyback-cli modules <module>` describes one module, socket by socket.
- `flyback-cli compare` says whether two patches are the same instrument.
- `install.sh` installs, updates or uninstalls the latest release on Linux, macOS and Windows.

### Viewer and full screen
- A web viewer plays a shipped preset or a patch file in a browser, picture and sound, and each preset on the preset site plays there from its page.
- A patch's knobs can be played over the full-screen picture, which Settings → Graphics sends to another monitor.
- F11 switches the viewer to full screen, and a patch with no picture opens as just its buttons and knobs.
- F3 over a full-screen picture, or `flyback-viewer --stats`, shows its frames a second.
- Settings → Graphics → Controls, or `flyback-viewer --transport bottom`, puts the transport at the bottom and the knobs at the top.

### Assistant
- The assistant keeps its conversation when a knob is turned or an edit undone; only adding or removing a module or a wire starts a new one.
- Stopping the assistant mid-turn no longer breaks the conversation, and it spends fewer tokens.
- Settings → Agent is now Settings → Assistant, where what the assistant is told and what it looks up can each be shown in its conversation.
- An admin key is refused, an exported key is not sent over plain http to another machine, and a message with your key in it is not sent.

### Canvas and interface
- A seek bar on the toolbar and the transports moves the patch's clock along its own length, and stops or loops at its end.
- Every socket and setting on a module says what it is for, in the panel.
- A patched socket in the panel names the socket at the other end of its wire, and a group's socket is its module's own row.
- Shift+drag moves modules into or out of a group, Ctrl+G keeps the one group name among them, and a box's socket is named for the socket inside.
- Double-clicking a box looks inside it without opening it.
- Ctrl+drag takes a wire off an output with any number of wires on it.
- Settings → Canvas → Compact modules puts each input beside an output on one row.
- Dragging the right button on an unpatched input turns its value, and a turning knob holds the pointer still.
- Settings → Files → Library sets a folder where a patch's sound files and pictures are also looked for.
- Saving a preset under a name already saved asks first.
- The status bar's envelope writes to Flyback's author.
- A dragged side panel comes back at the width it was left at.
- Patches, bundles, text files and plugin packages each have an icon, and on macOS a file opened from Finder or the Dock opens in Flyback.
- Usage statistics also count which modules are picked and which editor features a run uses.

### Performance
- Windows draws through the graphics card's own OpenGL, and Settings → Graphics → Driver goes back to Direct3D.
- A large patch's sound runs more than twice as fast, in the editor, the preset gallery and a render.
- A large patch no longer freezes the window while its picture is rebuilt, and an opened patch starts its sound and picture together.
- The preset gallery opens faster, and keeps its pictures between runs.
- AVI clips encode three times faster.

### Fixes
- Noise no longer sticks at full level after a day of playing, and the picture no longer stutters after hours.
- Fracture's sparks light the right squares.
- A malformed text patch is refused instead of closing the editor.
- Renaming a module or a group puts the caret beside the name.

## 0.4.0 — 2026-09-21

256 commits since 0.3.0.

### Updates
- Flyback updates itself: it checks for a signed release at startup, installs it the next time it starts and shows what changed. On by default, in the new Settings → Updates tab.
- Settings → Files picks what opens a .fbk, .fbkb or .fbks file: nothing, the editor or the viewer.
- Flyback counts how it is used, and nothing about you, your machine or your patches. On by default, in the new Settings → Usage tab.

### Plugins
- A plugin built for a Flyback that has since moved on is left out at startup, and About says whether the plugin or Flyback is the one to update.
- A plugin can paint its own module's background, in the new Settings → Canvas tab.
- Settings → Sound and Settings → MIDI name the plugin playing the sound and hearing the keyboard.

### Modules
- Added Stroke, Fade, Wander, Drum, Hiss, Bell and FM to Voice.
- Added Desk (a four-channel mixer), Duck (a sidechain) and Echo (a delay counted in steps of the tempo).
- Added Send and Receive, which carry a signal across the patch on a named bus.
- Added Expression, one block that computes a formula you type over its four inputs. Arithmetic in the text view is one, and Add, Multiply, Floor and the other one- and two-input Maths modules are Expressions now.
- Added Trails, Blur, Transform, Ink and Vignette for the picture, and Tune, a Quantiser and a Note in one.
- Added Text to Picture: lines of text in one of two pixel fonts, typed out by a signal.
- Euclid has a `stroke` output, and Tempo has `beats`.
- Added the Mastering plugin: EQ, Width, Crossover, Compressor, Limiter, Maximizer and a LUFS meter.
- The computer keyboard can be laid out by scale, chosen on any MIDI In, and Settings → MIDI picks what the first MIDI In gets.
- Filter, Random, Slew, Drive, Delay and Reverb are built into Flyback, so a patch can use them with no plugin installed.
- Mixer, Desk, Send and Receive have a section of their own, Routing.

### Presets
- Mycelium: a psybient track of ninety-six bars, and the largest preset.
- Bronze: a gamelan in a five-note pelog tuning, with a mandala struck by the same beats.
- Outrun: a synthwave track under a drawn scene of a sun, a ridge and a grid.
- Phase: phase music after Steve Reich, with a picture of two dials.
- Fracture: drum and bass at 170 with a synthesized break, chopped and reversed.
- Dub: dub techno to play, with four keys of chord and eight panel knobs.
- Overworld: a chiptune track under a side-scroller drawn a pixel at a time.
- Captions, Dodge (a game played on the keys Z to M) and Duck join the Picture and Effects plugins.
- Acid is a whole track of a hundred and twenty-eight bars, Whole band is a whole song, and Slow weather is rebuilt around five feedback loops.
- The other presets are rebuilt on the new modules, a seventh to a third fewer modules in the big ones, sounding and looking the same, and the teaching presets are updated.
- The preset button opens a gallery of tiles that filters as you type and plays a preset when the pointer rests on it, sorted under headings, with your own presets saved at the end.
- Settings → Graphics picks which preset Flyback opens on.

### Recording and export
- Recording counts in and starts at zero, and exports MP4, WebM, MOV, MP3, M4A and FLAC through ffmpeg; `flyback-cli render` takes the format from the extension, and `--loudness` reports LUFS.
- A Scope or an Analyzer is drawn in an exported clip.

### Viewer
- A third program, `flyback-viewer`, opens a patch and plays it: picture and sound, no editor, nothing written.

### Assistant
- The assistant is told what every module does again, within a budget Settings → Agent sets.
- Settings → Agent has a Probe this model button.
- The assistant can change the largest presets and read the presets for ideas.
- A long block in the transcript arrives folded, every frame the assistant renders is shown under its caption, and the panel is a column beside the patch.

### Canvas and interface
- Ctrl+scroll, Ctrl+plus and Ctrl+minus change the text view's font size, and Ctrl+0 resets it.
- A module wears its category's color and a mark of its own, drawn the same on the canvas, in the module list and in the panel.
- Ctrl+Shift+L, or Ctrl+click on Tidy, lays out only the selected modules.
- A toolbar button swaps the preview and the canvas.
- Ctrl+P pauses and plays the patch, and the full-screen preview has the viewer's controls.
- An Expression's formula is red while it does not read, and says what stopped it.
- Unsaved work survives a crash and is restored at the next start.
- Ctrl+B switches a module or a box off, and holding the right button does it until you let go.
- About has a bitcoin address to donate to, as a QR code.
- Flyback reopens as you left it: window, panels, text view and swapped preview.
- Ctrl+O and Ctrl+S open and save, and Ctrl+D duplicates the selection.
- Esc backs out of a drag on the canvas.
- The status bar shows the time as minutes and seconds, as in 1:05.25.

### Performance
- An output with no wire on it no longer costs anything, and the big presets run 2–19% fewer operations a sample.

### Fixes
- Opening a patch takes the sound back to zero along with the picture.
- A list on a module's panel keeps a pick back to the option it opened on.
- A filter, reverb, delay or loop with silence going into it no longer costs more than one with a signal, which could make a patch with resting parts crackle.
- Tidy no longer mislays modules on a patch too tall or too wide for the canvas.
- A bundle saved by a newer Flyback, or using a module you do not have, is refused rather than opened as a partial patch that the next save would write over it.
- Answering "Save…" and then saving a text copy no longer closes the patch as if it had been saved.
- Fading Volume to zero during a recording no longer stops the file there.
- A module moved with a middle-button pan or a zoom in the middle of the drag stays under the pointer, and the move can be undone.
- In text, `tempo(bpm: 104).beats` is read rather than dropped for the first output, anything left on a line after its statement is a complaint, and `%` takes the remainder Modulo does.
- Flyback no longer closes while the settings window is open.
- A corrupt sample or picture, a number too large for a number box, Escape after "Learn MIDI controller" found no device, and a frame too large for `flyback-cli render --size` are reported rather than crashing.
- Sign answers 0 for a value that is not a number, and agrees with the GPU about an infinite one.
- The macOS bundle carries the real version.
- A Flyback built from a source archive or a plain `docker build` no longer updates itself or counts its use.
- Several other bugfixes.

## 0.3.0 — 2026-09-17

71 commits since 0.2.0.

### Feedback loops
- Loops now draw as well as sound. A loop carries each pixel's value from the previous frame, on both the CPU renderer and the GPU shader.
- Removed the Unit Delay module. The wire that closes a loop is the delay, and the canvas draws it dashed.
- A module can be wired to itself, and the wire is drawn beneath the module.
- The wire a loop is cut at no longer changes when a module is dragged. Wires whose input sits left of their output are drawn as a U-turn.

### Knobs and MIDI
- A patch carries a panel of knobs, shown under the canvas with Ctrl+K. Knobs can be added, renamed and removed, and reordered by dragging their names or from their menu.
- Any unwired socket can follow a knob over its own range: click the knob's name, then socket rows on the canvas. The inspector shows a linked socket's knob and range.
- Turning a knob on screen or on a bound MIDI controller changes the playing patch without a recompile. A knob learns its controller from its menu, and the MIDI settings tab chooses whether a controller jumps or picks up.
- The panel wraps knobs onto more rows and is resized by dragging the splitter above it.

### Modules
- Added Random (white and pink noise, stepped or drifting values), Slew, Decay and Euclid to Voice.
- Added String, a Karplus-Strong plucked-string voice.
- Added Layer (eight blend modes through a mask) and Line (distance to a segment) to Picture.
- Added Analyzer, which charts the spectrum of the audio output on a log frequency axis.
- Added the Euclid kit preset. Played is rebuilt as four plucked strings into a reverb and moves to the Effects plugin.
- The Output loses its scan knobs, and the speakers are always evaluated at the origin. The picture is heard through a Scan instead, and Coordinates gains an `aspect` output.
- The Output's gain is renamed Volume. Turning it to zero closes the audio device, which replaces the Audio on/off toggle.
- Color inputs, and the Output's left and right, lose a slider that could only offer a grey or a hum, and take a wire only.

### Performance
- The CPU runs a patch as compiled IL once it has been built, and interprets it until then with no audible or visible hand-over. Frames run 1.5–2.1x faster and audio callbacks about 1.7x. A knob move rebinds without recompiling, and `--interpreted` keeps a run on the interpreter.

### Settings
- The settings window has a tab per section (Graphics, Recording, Sound, MIDI and Agent) with Save and Cancel. Choices are kept in `output.json` beside `assistant.json` and applied at startup.
- Graphics: output size, now with 1440p, 4K, 4:3, square, portrait and ultrawide; GPU or CPU rendering; and an optional cap on the preview's frame rate. The live sound's aspect follows the chosen size.
- Recording: a take's frame rate and JPEG quality.
- Sound: latency and the output device, applied on Save while the sound carries on. WASAPI, CoreAudio and ALSA list their devices, and System default follows the system's default device when it changes.
- Agent: how many turns a conversation may have.

### Recording and playback
- Record and Rewind move from the Output's panel to the toolbar, with glyphs, and Ctrl+R starts or stops a take.
- Removed the Output panel's Export button. `flyback-cli render` writes the same files.
- Rewind clears the playing program's memory on the audio thread, so it no longer sets off a loud, clipped burst.

### Assistant
- Conversations are saved with their patch: inside a bundle, or alongside a `.fbk`/`.fbks` in the user's data folder.
- Added a New conversation button that starts over on the same patch.
- None can be picked as a provider, and a saved provider that fails to load falls back to None.

### Opening files
- Open a patch by dropping it on the window or passing it on the command line.
- macOS opens `.fbk`, `.fbkb` and `.fbks` from Finder. Linux gets a `.desktop` file and MIME package for "Open With".

### Command line
- Added `print`, which writes any patch as text, with `--check` to confirm the printing compiles to the same program.
- Added `modules`, which lists the installed modules by provider, with `--json`.
- `check --strict` fails on warnings, `pack` supports `--json`, and the CLI supports shell completion.

### Canvas and interface
- The middle button pans while a wire or other drag is in progress, and the drag carries on afterwards.
- A dialog raised while the window is in the background flashes the taskbar, dock or window-list entry. Every dialog casts a shadow.
- The status bar shows the preview's frames per second and which renderer is actually drawing.
- The layout places a group as a single block, and a tidied patch is centered on the canvas.
- The canvas is 15000×10000, and zoom goes out to an eighth.
- Ctrl+E opens every group in the selection, and Ctrl+Shift+E closes them.
- Plugin information moves from the status bar into About. The status bar shows the report on the left and wires and time on the right.
- A printing of a patch with no bindings starts on its first statement rather than a blank line.

## 0.2.0 — 2026-09-10

76 commits since 0.1.0.

### Text language and live coding
- Added a text language that parses to a patch: lexer, parser, binder and step notation, specified in `docs/language.md`.
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
