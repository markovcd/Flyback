# Architecture decision records

Each record captures one decision, the situation that forced it, and what it
costs. Format is [Michael Nygard's](https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions):
context, decision, consequences.

Eighteen records — [0003](0003-cpu-rendering-with-a-gpu-path-left-open.md),
[0004](0004-visual-patch-editor-as-the-authoring-model.md),
[0026](0026-modules-from-plugins-with-provenance-in-the-file.md),
[0027](0027-delay-lines-give-the-audio-path-a-memory.md),
[0031](0031-a-sequencer-is-eight-inputs-and-no-memory.md),
[0033](0033-patches-authored-by-an-agent-behind-the-plugin-boundary.md),
[0034](0034-settings-in-a-file-the-key-in-the-operating-system.md),
[0036](0036-export-video-as-motion-jpeg-in-an-avi.md),
[0037](0037-one-output-block-that-every-patch-has.md),
[0038](0038-a-sequencers-notes-are-a-list-on-the-node.md),
[0040](0040-a-probe-is-a-second-compile-root.md),
[0041](0041-a-plugin-can-hold-state-without-a-new-opcode.md),
[0043](0043-a-scan-is-a-probe-read-backwards.md),
[0044](0044-lay-patches-out-in-layers-not-with-springs.md),
[0045](0045-what-is-copied-is-a-patch-file.md),
[0046](0046-the-module-list-is-a-gesture-not-a-panel.md) and
[0047](0047-the-agent-may-listen-where-the-model-can.md) and
[0048](0048-time-is-seconds-and-nothing-else.md) — are marked
**user-directed**: they were chosen by the project owner, not derived. The first
two sit upstream of almost everything else here; 0026 and 0027 each give up a
property an earlier record relied on, knowingly; 0031, 0033, 0034, 0036 and 0040
each let the instrument do something it could not do at all — 0040 lets it be
looked at; and 0037 and 0038 each take something away — 0037 retires a rule 0022
established, and 0038 supersedes the central decision of 0031, which is
user-directed itself. 0041 is the odd one: it takes nothing away and adds no
machinery, and simply finds that a rule 0027 stated as a limit had stopped being
true. 0043 is 0040 turned round: that one let the instrument be looked at, and
this one lets a picture be listened to, out of the same mechanism upside down.
0044 is the odd one of these: it was asked for as a physics simulation and is
recorded as the layered drawing that was proposed instead, which is the part
worth keeping. 0045 adds nothing to the format it uses: what a selection is
written as turns out to be what a patch is already written as, and the record is
mostly about what that buys. 0046 is the only one of these that takes something
away rather than adding: a panel, and with it the last part of the shell that
stood open whether or not it was wanted. 0047 closes something 0033 recorded as
open and left open on purpose, and closes it sideways: the model doing the
building still cannot hear, and is handed what a second model heard instead.
0048 takes a knob off a module and is the first record where the agent of 0033
is the *evidence* rather than the subject: it kept setting a rate low, which was
a fair reading of a control that should not have existed. All are recorded even
though they were not mine to make.

[0035](0035-a-glsl-backend-for-the-video-path.md) is the first record where two
backends disagree by design: the picture on screen and the picture in an exported
PNG are computed by different machines, at different precisions, and are allowed
to differ in their last bits.

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
| [0048](0048-time-is-seconds-and-nothing-else.md) | Time is seconds, and nothing else *(user-directed)* |

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
| [0038](0038-a-sequencers-notes-are-a-list-on-the-node.md) | A sequencer's notes are a list on the node *(user-directed)* |
| [0037](0037-one-output-block-that-every-patch-has.md) | One Output block, which every patch has *(user-directed)* |
| [0041](0041-a-plugin-can-hold-state-without-a-new-opcode.md) | A plugin can hold state without a new opcode *(user-directed)* |
| [0042](0042-the-clock-and-the-memory-flag-belong-to-the-emitter.md) | The clock and the memory flag belong to the emitter |

### Boundaries

| # | Decision |
|---|---|
| [0019](0019-no-third-party-dependencies-in-the-engine.md) | No third-party dependencies in the engine |
| [0020](0020-json-patch-files-keyed-by-string-type-ids.md) | JSON patch files keyed by string type IDs |
| [0025](0025-platform-io-behind-loadable-plugins.md) | Platform I/O behind plugins loaded at run time |
| [0026](0026-modules-from-plugins-with-provenance-in-the-file.md) | Modules may come from plugins, and the file records which *(user-directed)* |
| [0028](0028-publish-one-platform-at-a-time.md) | Publish one platform at a time, with only that platform's plugins |
| [0033](0033-patches-authored-by-an-agent-behind-the-plugin-boundary.md) | Patches may be authored by an agent, behind the plugin boundary *(user-directed)* |
| [0034](0034-settings-in-a-file-the-key-in-the-operating-system.md) | Settings in a file, the key in the operating system's store *(user-directed)* |
| [0047](0047-the-agent-may-listen-where-the-model-can.md) | The agent gets an ear, which is a second model *(user-directed)* |
