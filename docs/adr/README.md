# Architecture decision records

Each record captures one decision, the situation that forced it, and what it
costs. Format is [Michael Nygard's](https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions):
context, decision, consequences.


## Index

### Scope and shape

| # | Decision |
|---|---|
| [0001](0001-target-net-10.md) | Target .NET 10 and current C# |
| [0002](0002-split-engine-from-shell.md) | Split the engine from the UI shell |
| [0003](0003-cpu-rendering-with-a-gpu-path-left-open.md) | Render on the CPU, leave a GPU backend possible *(user-directed)* |
| [0004](0004-visual-patch-editor-as-the-authoring-model.md) | Author patches in a visual node editor *(user-directed)* |

### The engine

| # | Decision |
|---|---|
| [0005](0005-compile-to-a-flat-register-machine.md) | Compile patches to a flat register machine |
| [0006](0006-scalar-interpreter-parallel-over-rows.md) | Scalar interpreter parallelised over rows, not SIMD |
| [0007](0007-register-slots-with-scalar-broadcast.md) | Values are register slots; scalars broadcast to colors |
| [0008](0008-modules-as-data-in-one-catalogue.md) | Modules are data in a single catalogue |
| [0009](0009-editable-defaults-on-every-input.md) | Every input port carries an editable default |
| [0010](0010-any-typed-ports-for-polymorphic-maths.md) | `Any`-typed ports make maths modules polymorphic |
| [0011](0011-compile-backwards-from-output.md) | Compile backwards from the Output node |
| [0012](0012-feedback-as-a-module-not-a-cycle.md) | Feedback is an explicit module, not a graph cycle |
| [0013](0013-guard-arithmetic-instead-of-propagating-nan.md) | Guard arithmetic instead of propagating NaN |
| [0014](0014-coordinate-and-value-conventions.md) | Coordinate and value conventions |
| [0021](0021-recompile-the-whole-patch-on-every-edit.md) | Recompile the whole patch on every edit |
| [0031](0031-a-sequencer-is-eight-inputs-and-no-memory.md) | A sequencer is eight inputs and no memory *(user-directed)* |
| [0032](0032-the-registers-are-double-precision.md) | The registers are double precision |
| [0035](0035-a-glsl-backend-for-the-video-path.md) | A GLSL backend for the video path |
| [0040](0040-a-probe-is-a-second-compile-root.md) | A probe is a second compile root, not a second machine *(user-directed)* |
| [0043](0043-a-scan-is-a-probe-read-backwards.md) | A Scan is a Probe read backwards *(user-directed)* |
| [0056](0056-a-patch-can-be-played-and-what-plays-it-is-one-opcode.md) | A patch can be played, and what plays it is one opcode *(user-directed)* |
| [0048](0048-time-is-seconds-and-nothing-else.md) | Time is seconds, and nothing else *(user-directed)* |
| [0050](0050-normalled-sockets-carry-a-signal-with-no-wire.md) | Normalled sockets carry a signal with no wire *(user-directed)* |
| [0051](0051-a-quantisers-scale-is-a-set-on-the-node.md) | A quantiser's scale is a set on the node *(user-directed)* |
| [0052](0052-a-patch-names-its-samples-rather-than-carrying-them.md) | A patch names its samples rather than carrying them *(user-directed)* |
| [0059](0059-a-picture-comes-in-as-a-texture.md) | A picture comes in as a texture *(user-directed)* |
| [0053](0053-a-scope-records-what-the-speakers-played.md) | A Scope records what the speakers played *(user-directed)* |
| [0058](0058-the-picture-is-told-how-loud-the-sound-is.md) | The picture is told how loud the sound is *(user-directed)* |
| [0054](0054-what-a-module-carries-is-a-part-not-a-subtype.md) | What a module carries is a part, not a subtype *(user-directed)* |
| [0055](0055-a-plugins-extra-declares-its-editor.md) | A plugin's extra declares its editor *(user-directed)* |
| [0061](0061-what-a-module-carries-is-kept-in-one-store.md) | What a module carries is kept in one store *(user-directed)* |
| [0062](0062-indexed-polyphonic-midi-voices.md) | MIDI input is polyphonic through indexed voices *(user-directed)* |
| [0057](0057-a-shape-is-a-distance-and-one-module-inks-it.md) | A shape is a distance, and one module inks it *(user-directed)* |
| [0064](0064-a-pixel-runs-only-what-a-pixel-changes.md) | A pixel runs only what a pixel changes |

### The shell

| # | Decision |
|---|---|
| [0015](0015-avalonia-for-the-ui-shell.md) | Avalonia for the UI shell |
| [0016](0016-build-the-ui-in-c-sharp-without-xaml.md) | Build the UI in C#, without XAML |
| [0017](0017-draw-the-node-editor-in-one-control.md) | Draw the node editor in one custom control |
| [0018](0018-never-render-frames-on-the-ui-thread.md) | Never render frames on the UI thread |
| [0039](0039-one-window-class-across-a-file-per-region.md) | One window class, across a file per region |
| [0044](0044-lay-patches-out-in-layers-not-with-springs.md) | Lay patches out in layers, not with springs *(user-directed)* |
| [0045](0045-what-is-copied-is-a-patch-file.md) | What is copied is a patch file *(user-directed)* |
| [0046](0046-the-module-list-is-a-gesture-not-a-panel.md) | The module list is a gesture, not a panel *(user-directed)* |

### Sound

| # | Decision |
|---|---|
| [0022](0022-audio-and-video-are-two-sinks-over-one-patch.md) | Audio and video are two sinks over one patch |
| [0023](0023-oversample-the-audio-path.md) | Oversample the audio path rather than band-limiting modules |
| [0024](0024-audio-device-in-the-shell.md) | Sample generation in the engine, the audio device in the shell |
| [0027](0027-delay-lines-give-the-audio-path-a-memory.md) | Delay lines give the audio path a memory *(user-directed)* |
| [0029](0029-linux-sound-through-alsa.md) | Linux sound through ALSA, on a thread of our own |
| [0030](0030-oscillators-accumulate-their-phase.md) | Oscillators accumulate their phase on the audio path |
| [0036](0036-export-video-as-motion-jpeg-in-an-avi.md) | Export video as Motion JPEG in an AVI *(user-directed)* |
| [0049](0049-record-the-gpu-frame-not-the-interpreter.md) | Record the GPU frame, not the interpreter *(user-directed)* |
| [0038](0038-a-sequencers-notes-are-a-list-on-the-node.md) | A sequencer's notes are a list on the node *(user-directed)* |
| [0037](0037-one-output-block-that-every-patch-has.md) | One Output block, which every patch has *(user-directed)* |
| [0041](0041-a-plugin-can-hold-state-without-a-new-opcode.md) | A plugin can hold state without a new opcode *(user-directed)* |
| [0042](0042-the-clock-and-the-memory-flag-belong-to-the-emitter.md) | The clock and the memory flag belong to the emitter |

### Boundaries

| # | Decision |
|---|---|
| [0019](0019-no-third-party-dependencies-in-the-engine.md) | No third-party dependencies in the engine |
| [0020](0020-json-patch-files-keyed-by-string-type-ids.md) | JSON patch files keyed by string type IDs |
| [0060](0060-a-bundle-is-a-patch-and-what-it-names.md) | A bundle is a patch and what it names *(user-directed)* |
| [0025](0025-platform-io-behind-loadable-plugins.md) | Platform I/O behind plugins loaded at run time |
| [0026](0026-modules-from-plugins-with-provenance-in-the-file.md) | Modules may come from plugins, and the file records which *(user-directed)* |
| [0028](0028-publish-one-platform-at-a-time.md) | Publish one platform at a time, with only that platform's plugins |
| [0063](0063-one-plugin-per-platform-for-sound-and-midi.md) | One plugin per platform, carrying its sound and its MIDI *(user-directed)* |
| [0033](0033-patches-authored-by-an-agent-behind-the-plugin-boundary.md) | Patches may be authored by an agent, behind the plugin boundary *(user-directed)* |
| [0034](0034-settings-in-a-file-the-key-in-the-operating-system.md) | Settings in a file, the key in the operating system's store *(user-directed)* |
| [0047](0047-the-agent-may-listen-where-the-model-can.md) | The agent gets an ear, which is a second model *(user-directed)* |
