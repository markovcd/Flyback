# Flyback glossary

One word for each thing, and the same word everywhere a person reads: the UI,
the site, the docs, ADRs, comments, test names and commit messages.

- **Say** is the word. Use it in prose, whatever the code calls the thing.
- **Code** is the name in source. Where it differs from **Say**, it differs
  because it is part of the plugin contract or the file format, and renaming it
  would break a plugin or a saved patch. A comment on it still uses **Say**.
- **Not** lists words that must not stand for the thing. Some of them mean
  something else here, and that is the reason.

The words come from the instrument rather than the framework, so a person who
has patched a hardware synthesizer should already know most of them.

- [The patch](#the-patch)
- [Picture and sound](#picture-and-sound)
- [Inside a program](#inside-a-program)
- [The editor](#the-editor)
- [Playing and recording](#playing-and-recording)
- [Files](#files)
- [Plugins](#plugins)
- [The text language](#the-text-language)
- [Spelling](#spelling)

---

## The patch

| Say | Means | Code | Not |
|---|---|---|---|
| **patch** | Everything a person builds: modules, wires, groups and the knob panel. One patch makes one picture and one sound. | `Patch` | graph, project, program |
| **module** | One thing on the canvas: a Sine, an Add, the Output. Both the kind and the one placed in a patch are "a module". | `NodeDef` (the kind), `NodeInstance` (one in a patch) | node, unit, component, block (but see [block](#the-editor)) |
| **type id** | A module kind's permanent name: `category.name` for a built-in, `<provider id>.name` for a plugin's. Never changes once shipped, because saved patches name it. | `TypeId` | id alone, where a module's instance id could be meant |
| **name** | What a module is called on the canvas. Its kind's name until renamed; renaming never touches the type id. | `NodeDef.Name`, `NodeInstance.Name` | label, title |
| **category** | The family a module is listed and drawn under: Sources, Oscillators, Math, Output and the rest. | `ModuleCategories` | family, section, group |
| **catalog** | Every module this run knows: the built-ins plus what plugins added. | `NodeCatalog` (built-ins), `ModuleCatalog` (the whole set) | catalogue |
| **socket** | An input or an output on a module. | `PortSpec`, `PortKind`, a port index | port, jack, pin, inlet, outlet |
| **input**, **output** | The two kinds of socket. | `Inputs`, `Outputs` | — |
| **description** | What a module is for and how its sockets work together. It heads the inspector and follows the module in the assistant's briefing. | `NodeDef.Description` | summary, blurb |
| **help** | What one socket is for, in words that stand alone: the tip on its row in the inspector, and the line after its name the assistant reads. | `PortSpec.Help` | tooltip (that is where it shows), hint, doc |
| **standard socket** | A socket that means the same on every module, described once: `x`, `freq`, `mix` and the rest, and every domain input. A socket opts in with `Standard = true`, and asking for a name with no standard is refused. | `SocketHelp` | common port, default help |
| **Output** | The one module every patch has. Its `color` goes to the screen; its `left`, `right` and `volume` go to the speakers. Capitalized, because it is a module's name. | type id `output` | output block, master, sink (see [sink](#picture-and-sound)) |
| **wire** | Joins an output socket to an input socket. | `Connection` | connection, cable, edge, link, patch cord |
| **knob** | The value an input socket holds when nothing is wired into it, dialed on the canvas and in the inspector. | `InputValues`; its starting value is `PortSpec.Default` | parameter, param, setting, value alone |
| **normalled** | An input with no wire that still carries a signal: from a hidden module such as `time`, or from an earlier input, as the Output's `right` carries `left`. | `NormalledTo`, `NormalledFrom` | normaled, default-connected |
| **switched off** | A module that passes its first input straight through, as if it were a wire. | `Off` | bypassed, muted, disabled |
| **group** | A named set of modules drawn as one box. | `NodeGroup` | subgraph, macro |
| **box** | A group as it is drawn: open, showing its modules, or shut, drawn as one. | — | frame, container |
| **bus** | A named connection between a Send and every Receive that shares its name: a wire with no line drawn. | `Buses` | channel, send (the module is the Send) |
| **Expression** | The module that computes a typed formula over four inputs. Arithmetic in the text language and in presets arrives as one. | type id `math.expression` | formula module |
| **what a module carries** | Anything a module holds that is not a knob: a sequencer's steps, a quantizer's scale, a Sample's file, a plugin's fields. Named by what it is ("its scale", "its file") wherever possible. | `NodeExtra`, `NodeInstance.State` | extra, state, payload |
| **field** | One thing a plugin's module carries, edited in the inspector. | `ExtraField` | property, attribute |
| **preset** | A patch that ships with Flyback or with a plugin, picked from the gallery. | `PatchPreset`, `Presets` | example, template, demo |
| **shared preset** | A preset from the preset site rather than from Flyback. | `PresetSite` | community preset, online preset |

## Picture and sound

The code says video and audio; prose says picture and sound. "Video" is right
only for a file format, and "audio" only where a platform or a device API is
meant.

| Say | Means | Code | Not |
|---|---|---|---|
| **picture** | What the screen shows. | "video" in identifiers: `CompileForVideo`, `SynthRenderer` | video, image, visuals, graphics |
| **sound** | What the speakers play. | "audio" in identifiers: `CompileForAudio`, `AudioRenderer` | audio (in UI and prose) |
| **the screen**, **the speakers** | Where the picture and the sound go: the two ends a patch is compiled for. | — | display, monitor (those are hardware the window sits on) |
| **sink** | The screen or the speakers, from the compiler's side. An engineering word; the UI never says it. | — | output, destination |
| **preview** | The live picture in the editor's window. | `PreviewHost`, `IPreviewSurface` | viewer, monitor, canvas |
| **Scope**, **Analyzer** | The two meters: what the speakers played over time, and its spectrum. | type ids `ScopeTypeId`, `AnalyzerTypeId` | oscilloscope, spectrum view |
| **Probe**, **Scan** | A module that reads a signal across a domain it substitutes, and a Probe read backwards, which is how a picture is heard. | `ProbeTypeId`, `ScanTypeId` | — |
| **voice** | One of the notes MIDI plays at once. | `MidiVoice` | channel, note |
| **live value** | A panel knob, a MIDI voice or a meter reading, which the program reads without being rebuilt. | `LiveValues` | parameter |
| **sample** | One value of sound at one instant. A sound file is a **sound file**, and the module that plays one is a **Sample**. | `ISampleLibrary` holds the files | sample for the file |

## Inside a program

Engineering words. They appear in docs, ADRs and comments, not in the UI.

| Say | Means | Code | Not |
|---|---|---|---|
| **program** | What a patch compiles to: one for the screen and one for the speakers. | `CompiledPatch` | patch, shader, script |
| **compile**, **rebuild** | Turning a patch into its programs, on every edit. | `PatchCompiler` | build (that is the text language's word, below) |
| **op**, **opcode** | One instruction of a program, and which instruction it is. | `Op`, `OpCode` | instruction, node |
| **register**, **slot** | A number a program holds; a register or three of them holding one value. | `Slot` | variable |
| **backend** | What runs a program: the interpreter (which is the specification), IL, or GLSL. | `CompiledPatch.Evaluate`, `IlProgram`, `GlslEmitter` | engine, runtime |
| **evaluation** | One run of a program: one pixel, or one sound sample at four times the rate. | — | tick, step |
| **frame** | One whole picture. | — | image |
| **stage** | When an op needs rerunning: once a frame, once a row, or once a pixel. | `FramePlan` | pass, phase |
| **domain** | The input a module is read across, such as `x` for an oscillator. | `PortSpec.Domain` | — |
| **delay line**, **cell**, **plane** | What a program remembers: a ring of past samples, one remembered number, one number per pixel. | `DelayState`, `PlaneState` | buffer, memory alone |
| **cycle** | A loop of wires, which carries one evaluation's delay. | `Cycles` | feedback (that is the Feedback module) |

## The editor

| Say | Means | Code | Not |
|---|---|---|---|
| **the editor** | The window a patch is built in, and the program that opens it. | `MainWindow`, `Flyback.exe` | the app (except for the whole product), the node editor (except as the type's name) |
| **canvas** | Where modules and wires are drawn. | `NodeEditor` | graph, board, workspace, node editor |
| **block** | A module as it is drawn: its face, header and mark. Only for the drawing. | `ModulePlate`, `ModuleWash` | block for the module in any other sense |
| **standout** | A module drawn as itself rather than as its category. | — | — |
| **skin** | A plugin's own drawing of its module. | `ModuleSkin` | theme |
| **inspector** | The panel that shows and edits what is selected. | `Inspector` | properties, sidebar |
| **palette** | The module list that opens on right-click or Space. | `ModulePalette` | module list, menu, browser |
| **knob panel** | The row of panel knobs, turned by hand or from a MIDI controller. | `ControlsPanel` | controls panel, mixer |
| **panel knob** | A knob on the knob panel. It moves every socket linked to it, and a MIDI controller can be learned for it. | `PatchControl`, `Patch.Controls` | control, macro, fader |
| **link** | A socket following a panel knob over a range. | `ControlLink` | binding, mapping |
| **text view** | The patch as text, over the canvas. | `SourceView` | code view, source view, editor |
| **the document** | Whichever of the canvas and the text owns the patch, decided by the file that was opened. | `Document` | — |
| **assistant** | The AI that edits a patch through the workbench, in a column beside it. | `IPatchAssistant`, `AssistantPanel` | agent (that is the one building Flyback), AI, copilot, bot |
| **workbench** | Everything an assistant may do to a patch, and its limits. | `PatchWorkbench` | tools |
| **conversation** | What was said to an assistant, saved with the patch it is about. | — | chat, thread, session |
| **Settings** | The settings window and its sections: Graphics, Canvas, Recording, Sound, MIDI, Assistant, Files, Updates, Usage. | `MainWindow` builds it | preferences, options |

## Playing and recording

| Say | Means | Code | Not |
|---|---|---|---|
| **the viewer** | `flyback-viewer`: plays a patch, picture and sound, and writes nothing. | `Flyback.Viewer`; its transport is `ViewerPlayer` | player (a Sample is the only player) |
| **transport** | Play, pause and rewind. | — | — |
| **rewind** | Take the patch back to zero seconds, in the picture and the sound. | — | reset, restart |
| **record**, **take** | Recording the patch live from the editor (Ctrl+R), and the file one recording makes. | `Takes`, `LiveRecorder` | capture, clip |
| **render** | Writing a patch to a file from the command line, exactly and at any speed. | `RenderCommand` | export (except as the menu's word for it), record |
| **still**, **clip** | What a render writes: one picture, or picture and sound over a length of time. | — | screenshot, video |

## Files

| Say | Means | Code | Not |
|---|---|---|---|
| **patch file** | `.fbk`: the patch as JSON. | `PatchIO` | document (unless the document is meant), project file |
| **bundle** | `.fbkb`: a patch file with the sound files and pictures it names, and optionally its conversation. | `PatchBundle` | archive, package |
| **text** | `.fbks`: the patch written in the Flyback language. | `PatchLanguage` | script, source |
| **plugin package** | `.fbkp`: a signed plugin, ready to install. | `PluginPackage` | bundle |
| **recovery** | The snapshot of unsaved work each window keeps, reopened after a crash. | `Recovery` | autosave, backup |

## Plugins

| Say | Means | Code | Not |
|---|---|---|---|
| **plugin** | A folder under `plugins/` holding one class that registers modules, presets, sound or MIDI backends, secret stores or assistants. | `IFlybackPlugin` | extension, add-on |
| **provider**, **provider id** | Who a plugin's modules belong to, and the prefix of their type ids. | `ModuleProvider` | vendor, namespace |
| **contract** | What a plugin is compiled against, with a version of its own. | `PluginContractVersion` in Directory.Build.props | API alone, SDK |
| **plugin problem** | A plugin, or a part of one, that was refused, and why. | `PluginProblem` | error, failure |
| **plugin site**, **preset site** | Where shared plugins and shared presets are published and found. | `PluginSite`, `PresetSite` | store, marketplace, repository |
| **letter** | What somebody writes to Flyback's author from the status bar, and the site takes. | `SiteLetters`, `LetterStore` | feedback (that is the Feedback module), comment, ticket |

## The text language

Its own words are in [language.md](language.md); these are the ones that meet
the rest of Flyback.

| Say | Means | Code | Not |
|---|---|---|---|
| **short name** | A module's name in text: the last segment of its type id. | — | alias |
| **binding** | `let NAME = …`: a name given to a module. | `let` | variable |
| **pipe** | `a \|> b`: `a` into `b`'s `in`, its only socket, its leading `x` and `y`, its one color socket for a color, or the socket written `_`. | `\|>` | chain |
| **placeholder** | `_` as an argument, `adsr(gate: _)`: the socket a pipe lands on. | `_` | hole, wildcard |
| **def** | A subgraph with holes in it, written once and used many times. Exists only in text; building it leaves modules. | `def` | function, group (a group is on the canvas) |
| **build** | Text into a patch. Exact. | `Binder.Build` | compile (that is a patch into programs), parse |
| **print** | A patch into text. Lossy on purpose. | `PatchPrinter` | export, serialize |

## Spelling

American, in every surface. The ones this repo gets wrong most:

| Say | Not |
|---|---|
| catalog | catalogue |
| center, centered | centre, centred |
| gray | grey |
| quantize, quantizer | quantise, quantiser |
| math | maths |
| color | colour |
| canceled | cancelled |
| zero | nought |

Two exceptions. **Normalled** is the synthesizer term of art, so it keeps its
double *l*. And a shipped **type id** keeps its spelling forever
(`audio.quantiser`), because saved patches name it: its display name moves,
the id does not.
