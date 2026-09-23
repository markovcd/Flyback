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
| [0065](0065-a-text-language-that-parses-to-a-patch.md) | A text language, which parses to a patch *(user-directed)* |
| [0067](0067-a-module-keeps-its-name-and-its-memory-across-a-rebuild.md) | A module keeps its name, and its memory, across a rebuild *(user-directed)* |
| [0068](0068-the-file-that-was-opened-decides-who-owns-the-patch.md) | The file that was opened decides who owns the patch *(user-directed)* |
| [0119](0119-the-website-shows-a-module-by-photographing-one.md) | The website shows a module by photographing one *(user-directed)* |
| [0123](0123-a-third-program-plays-a-patch-and-writes-nothing.md) | A third program plays a patch and writes nothing *(user-directed)* |

### The engine

| # | Decision |
|---|---|
| [0005](0005-compile-to-a-flat-register-machine.md) | Compile patches to a flat register machine |
| [0006](0006-scalar-interpreter-parallel-over-rows.md) | Scalar interpreter parallelised over rows, not SIMD |
| [0007](0007-register-slots-with-scalar-broadcast.md) | Values are register slots; scalars broadcast to colors |
| [0008](0008-modules-as-data-in-one-catalogue.md) | Modules are data in a single catalogue |
| [0009](0009-editable-defaults-on-every-input.md) | Every input port carries an editable default *(color ports and Output's `left` exempted by [0084](0084-a-socket-with-nothing-to-dial-gets-no-knob.md))* |
| [0010](0010-any-typed-ports-for-polymorphic-maths.md) | `Any`-typed ports make maths modules polymorphic |
| [0011](0011-compile-backwards-from-output.md) | Compile backwards from the Output node *(unread ops inside a reached module swept by [0096](0096-an-op-nothing-reads-is-left-out.md))* |
| [0012](0012-feedback-as-a-module-not-a-cycle.md) | Feedback is an explicit module, not a graph cycle *(cycles superseded by [0075](0075-a-cycle-carries-its-own-delay.md))* |
| [0013](0013-guard-arithmetic-instead-of-propagating-nan.md) | Guard arithmetic instead of propagating NaN |
| [0014](0014-coordinate-and-value-conventions.md) | Coordinate and value conventions |
| [0021](0021-recompile-the-whole-patch-on-every-edit.md) | Recompile the whole patch on every edit |
| [0031](0031-a-sequencer-is-eight-inputs-and-no-memory.md) | A sequencer is eight inputs and no memory *(user-directed)* |
| [0032](0032-the-registers-are-double-precision.md) | The registers are double precision |
| [0035](0035-a-glsl-backend-for-the-video-path.md) | A GLSL backend for the video path *(time carried in two floats by [0137](0137-the-clock-reaches-the-gpu-in-two-floats.md))* |
| [0137](0137-the-clock-reaches-the-gpu-in-two-floats.md) | The clock reaches the GPU in two floats *(user-directed)* |
| [0040](0040-a-probe-is-a-second-compile-root.md) | A probe is a second compile root, not a second machine *(user-directed)* |
| [0043](0043-a-scan-is-a-probe-read-backwards.md) | A Scan is a Probe read backwards *(user-directed; its Output knob superseded by [0077](0077-the-picture-is-heard-only-through-a-scan.md))* |
| [0056](0056-a-patch-can-be-played-and-what-plays-it-is-one-opcode.md) | A patch can be played, and what plays it is one opcode *(user-directed)* |
| [0048](0048-time-is-seconds-and-nothing-else.md) | Time is seconds, and nothing else *(user-directed)* |
| [0050](0050-normalled-sockets-carry-a-signal-with-no-wire.md) | Normalled sockets carry a signal with no wire *(user-directed)* |
| [0051](0051-a-quantisers-scale-is-a-set-on-the-node.md) | A quantiser's scale is a set on the node *(user-directed)* |
| [0052](0052-a-patch-names-its-samples-rather-than-carrying-them.md) | A patch names its samples rather than carrying them *(user-directed)* |
| [0059](0059-a-picture-comes-in-as-a-texture.md) | A picture comes in as a texture *(user-directed)* |
| [0053](0053-a-scope-records-what-the-speakers-played.md) | A Scope records what the speakers played *(user-directed)* |
| [0058](0058-the-picture-is-told-how-loud-the-sound-is.md) | The picture is told how loud the sound is *(user-directed)* |
| [0073](0073-an-analyzer-is-a-scope-filled-with-a-spectrum.md) | An Analyzer is a Scope filled with a spectrum *(user-directed)* |
| [0054](0054-what-a-module-carries-is-a-part-not-a-subtype.md) | What a module carries is a part, not a subtype *(user-directed)* |
| [0055](0055-a-plugins-extra-declares-its-editor.md) | A plugin's extra declares its editor *(user-directed; a fourth shape, text, added by [0104](0104-a-formula-is-one-block-and-exactly-the-modules-it-names.md) and given several lines by [0105](0105-text-is-a-shape-baked-into-a-picture.md))* |
| [0061](0061-what-a-module-carries-is-kept-in-one-store.md) | What a module carries is kept in one store *(user-directed)* |
| [0062](0062-indexed-polyphonic-midi-voices.md) | MIDI input is polyphonic through indexed voices *(user-directed)* |
| [0099](0099-the-computer-keyboard-can-be-laid-out-by-scale.md) | The computer keyboard can be laid out by scale, and the patch says how *(user-directed)* |
| [0086](0086-panel-knobs-are-read-as-live-values.md) | Panel knobs are read as live values, and a MIDI controller turns a knob rather than a socket *(user-directed)* |
| [0057](0057-a-shape-is-a-distance-and-one-module-inks-it.md) | A shape is a distance, and one module inks it *(user-directed)* |
| [0105](0105-text-is-a-shape-baked-into-a-picture.md) | Text is a shape, baked into a picture *(user-directed)* |
| [0095](0095-a-module-may-be-a-handful-of-others-if-it-is-exactly-them.md) | A module may be a handful of others, if it is exactly them *(user-directed; the Bell it left out counted again and added by [0097](0097-a-wrapping-module-carries-a-setting-where-what-it-wraps-differed.md); its "the engine's own presets may not need a plugin" reused for six primitives by [0128](0128-filter-random-slew-drive-delay-and-reverb-are-the-engines-own.md))* |
| [0097](0097-a-wrapping-module-carries-a-setting-where-what-it-wraps-differed.md) | A wrapping module carries a setting where what it wraps differed *(user-directed)* |
| [0130](0130-an-auto-remap-reads-its-ranges-off-its-wires.md) | An Auto remap reads its ranges off its wires *(user-directed)* |
| [0104](0104-a-formula-is-one-block-and-exactly-the-modules-it-names.md) | A formula is one block, and exactly the modules it names *(user-directed)* |
| [0106](0106-a-sum-in-the-text-is-one-expression.md) | A sum in the text is one Expression *(user-directed; its printing of every Expression as a call replaced by [0107](0107-an-expression-is-printed-as-the-sum-it-is.md))* |
| [0107](0107-an-expression-is-printed-as-the-sum-it-is.md) | An Expression is printed as the sum it is *(user-directed; a sum with a pipeline for an operand made a call by [0108](0108-a-preset-arrives-with-its-arithmetic-folded.md))* |
| [0108](0108-a-preset-arrives-with-its-arithmetic-folded.md) | A preset arrives with its arithmetic folded into Expressions *(user-directed; what it leaves alone narrowed by [0109](0109-the-expression-stands-for-the-small-maths-modules.md))* |
| [0109](0109-the-expression-stands-for-the-small-maths-modules.md) | The Expression stands for the small Maths modules *(user-directed)* |
| [0064](0064-a-pixel-runs-only-what-a-pixel-changes.md) | A pixel runs only what a pixel changes |
| [0074](0074-a-cell-is-a-plane-on-the-video-path.md) | A cell is a plane on the video path *(user-directed)* |
| [0075](0075-a-cycle-carries-its-own-delay.md) | A cycle carries its own delay *(user-directed)* |
| [0126](0126-a-bus-is-a-wire-with-no-cable.md) | A bus is a wire with no cable *(user-directed)* |
| [0076](0076-the-processor-runs-a-program-as-il-once-it-is-built.md) | The processor runs a program as IL once it is built *(user-directed)* |
| [0096](0096-an-op-nothing-reads-is-left-out.md) | An op nothing reads is left out *(user-directed)* |
| [0077](0077-the-picture-is-heard-only-through-a-scan.md) | The picture is heard only through a Scan *(user-directed; its aspect table's shell-export row retired by [0078](0078-export-leaves-the-shell-for-the-cli-that-already-writes-it.md); its Live-engine row amended by [0083](0083-the-live-engines-aspect-follows-the-preview.md))* |
| [0083](0083-the-live-engines-aspect-follows-the-preview.md) | The live engine's aspect follows the preview *(user-directed)* |
| [0084](0084-a-socket-with-nothing-to-dial-gets-no-knob.md) | A socket with nothing to dial gets no knob *(user-directed)* |

### The shell

| # | Decision |
|---|---|
| [0015](0015-avalonia-for-the-ui-shell.md) | Avalonia for the UI shell |
| [0016](0016-build-the-ui-in-c-sharp-without-xaml.md) | Build the UI in C#, without XAML |
| [0017](0017-draw-the-node-editor-in-one-control.md) | Draw the node editor in one custom control |
| [0018](0018-never-render-frames-on-the-ui-thread.md) | Never render frames on the UI thread |
| [0039](0039-one-window-class-across-a-file-per-region.md) | One window class, across a file per region |
| [0044](0044-lay-patches-out-in-layers-not-with-springs.md) | Lay patches out in layers, not with springs *(user-directed; what becomes of a drawing too large for the canvas settled by [0092](0092-a-drawing-too-wide-for-the-canvas-shuts-a-box.md); runnable over a selection by [0110](0110-the-layout-can-be-given-the-selection-instead-of-the-patch.md))* |
| [0045](0045-what-is-copied-is-a-patch-file.md) | What is copied is a patch file *(user-directed)* |
| [0046](0046-the-module-list-is-a-gesture-not-a-panel.md) | The module list is a gesture, not a panel *(user-directed)* |
| [0070](0070-a-preset-declares-no-coordinates.md) | A preset declares no coordinates *(user-directed)* |
| [0071](0071-two-undo-stacks-and-which-one-a-press-lands-on.md) | Two undo stacks, and which one a press lands on |
| [0078](0078-export-leaves-the-shell-for-the-cli-that-already-writes-it.md) | Export leaves the shell for the CLI that already writes it *(user-directed; its Output-panel `Record…` row moved to the toolbar by [0080](0080-record-moves-to-the-toolbar-with-a-glyph-and-ctrl-r.md); playing without the editor is [0123](0123-a-third-program-plays-a-patch-and-writes-nothing.md))* |
| [0080](0080-record-moves-to-the-toolbar-with-a-glyph-and-ctrl-r.md) | Record moves to the toolbar, with a glyph and Ctrl+R *(user-directed; its Output-panel `Rewind` row moved to the toolbar by [0081](0081-rewind-moves-to-the-toolbar-beside-record.md); its press given a count-in by [0090](0090-a-take-is-counted-in-and-starts-at-zero.md))* |
| [0081](0081-rewind-moves-to-the-toolbar-beside-record.md) | Rewind moves to the toolbar, beside Record *(user-directed; every take does it first by [0090](0090-a-take-is-counted-in-and-starts-at-zero.md))* |
| [0082](0082-the-output-settings-move-to-the-settings-window.md) | The Output settings move to the settings window, and are kept *(user-directed; its next-launch latency made immediate by [0085](0085-a-sound-backend-declares-its-own-settings.md))* |
| [0087](0087-the-assistant-moves-to-a-column-beside-the-patch.md) | The assistant moves to a column, beside the patch *(user-directed)* |
| [0090](0090-a-take-is-counted-in-and-starts-at-zero.md) | A take is counted in, and starts at zero *(user-directed; its fixed three seconds and unconditional rewind made settings by [0091](0091-how-a-take-begins-is-two-settings.md))* |
| [0091](0091-how-a-take-begins-is-two-settings.md) | How a take begins is two settings *(user-directed)* |
| [0092](0092-a-drawing-too-wide-for-the-canvas-shuts-a-box.md) | A drawing too wide for the canvas shuts a box, and one that cannot fit moves nothing *(user-directed)* |
| [0110](0110-the-layout-can-be-given-the-selection-instead-of-the-patch.md) | The layout can be given the selection instead of the patch *(user-directed)* |
| [0093](0093-a-startup-preset-is-a-graphics-setting.md) | A startup preset is a Graphics setting *(user-directed)* |
| [0103](0103-unsaved-work-outlives-a-crash.md) | Unsaved work outlives a crash *(user-directed)* |
| [0111](0111-the-panels-actions-are-a-row-of-glyphs.md) | The panel's actions are a row of glyphs *(user-directed)* |
| [0116](0116-a-module-is-drawn-as-its-category-and-a-standout-as-itself.md) | A module is drawn as its category, and a standout as itself *(user-directed)* |
| [0117](0117-a-module-switched-off-is-a-wire.md) | A module switched off is a wire *(user-directed)* |
| [0118](0118-a-plugin-paints-its-own-module-background.md) | A plugin paints its own module's background *(user-directed)* |
| [0121](0121-the-window-is-left-as-it-was-left.md) | The window is left as it was left *(user-directed)* |
| [0129](0129-full-screen-on-another-monitor-is-a-window-of-its-own.md) | Full screen on another monitor is a window of its own *(user-directed)* |
| [0122](0122-the-panel-wears-the-block-it-is-about.md) | The panel wears the block it is about *(user-directed; the face it borrows is [0116](0116-a-module-is-drawn-as-its-category-and-a-standout-as-itself.md))* |
| [0124](0124-what-two-shells-draw-with-is-a-project-of-its-own.md) | What two shells draw with is a project of its own *(user-directed)* |

### Sound

| # | Decision |
|---|---|
| [0022](0022-audio-and-video-are-two-sinks-over-one-patch.md) | Audio and video are two sinks over one patch |
| [0023](0023-oversample-the-audio-path.md) | Oversample the audio path rather than band-limiting modules |
| [0024](0024-audio-device-in-the-shell.md) | Sample generation in the engine, the audio device in the shell |
| [0027](0027-delay-lines-give-the-audio-path-a-memory.md) | Delay lines give the audio path a memory *(user-directed)* |
| [0029](0029-linux-sound-through-alsa.md) | Linux sound through ALSA, on a thread of our own |
| [0030](0030-oscillators-accumulate-their-phase.md) | Oscillators accumulate their phase on the audio path |
| [0036](0036-export-video-as-motion-jpeg-in-an-avi.md) | Export video as Motion JPEG in an AVI *(user-directed; demoted to the fallback by [0089](0089-ffmpeg-encodes-what-it-can-and-the-avi-is-the-fallback.md))* |
| [0089](0089-ffmpeg-encodes-what-it-can-and-the-avi-is-the-fallback.md) | ffmpeg encodes what it can, and the AVI is the fallback *(user-directed)* |
| [0049](0049-record-the-gpu-frame-not-the-interpreter.md) | Record the GPU frame, not the interpreter *(user-directed)* |
| [0038](0038-a-sequencers-notes-are-a-list-on-the-node.md) | A sequencer's notes are a list on the node *(user-directed)* |
| [0037](0037-one-output-block-that-every-patch-has.md) | One Output block, which every patch has *(user-directed; its `gain` socket renamed `volume` by [0079](0079-the-gain-knob-becomes-volume-and-nought-is-off.md); its picture settings moved to the settings window by [0082](0082-the-output-settings-move-to-the-settings-window.md))* |
| [0041](0041-a-plugin-can-hold-state-without-a-new-opcode.md) | A plugin can hold state without a new opcode *(user-directed; the filter itself, and five more like it, moved into the engine by [0128](0128-filter-random-slew-drive-delay-and-reverb-are-the-engines-own.md))* |
| [0042](0042-the-clock-and-the-memory-flag-belong-to-the-emitter.md) | The clock and the memory flag belong to the emitter |
| [0079](0079-the-gain-knob-becomes-volume-and-nought-is-off.md) | The gain knob becomes Volume, and nought is off *(user-directed)* |
| [0100](0100-mastering-is-a-plugin-of-stateful-primitives.md) | Mastering is a plugin of stateful primitives *(user-directed)* |
| [0101](0101-a-one-knob-maximizer-is-a-module-of-its-own.md) | A one-knob maximizer is a module of its own *(user-directed)* |
| [0125](0125-a-duck-is-a-sidechain-with-the-depth-on-a-knob.md) | A Duck is a sidechain with the depth on a knob *(user-directed)* |
| [0128](0128-filter-random-slew-drive-delay-and-reverb-are-the-engines-own.md) | Filter, Random, Slew, Drive, Delay and Reverb are the engine's own *(user-directed)* |

### Boundaries

| # | Decision |
|---|---|
| [0019](0019-no-third-party-dependencies-in-the-engine.md) | No third-party dependencies in the engine *(read as being about dependencies rather than about programs by [0089](0089-ffmpeg-encodes-what-it-can-and-the-avi-is-the-fallback.md))* |
| [0020](0020-json-patch-files-keyed-by-string-type-ids.md) | JSON patch files keyed by string type IDs |
| [0060](0060-a-bundle-is-a-patch-and-what-it-names.md) | A bundle is a patch and what it names *(user-directed)* |
| [0131](0131-shared-presets-live-on-a-site-that-only-reads-its-media.md) | Shared presets live on a site that only reads its media *(user-directed)* |
| [0136](0136-a-letter-goes-to-the-author-through-the-preset-site.md) | A letter goes to the author through the preset site *(user-directed)* |
| [0025](0025-platform-io-behind-loadable-plugins.md) | Platform I/O behind plugins loaded at run time |
| [0026](0026-modules-from-plugins-with-provenance-in-the-file.md) | Modules may come from plugins, and the file records which *(user-directed)* |
| [0028](0028-publish-one-platform-at-a-time.md) | Publish one platform at a time, with only that platform's plugins |
| [0063](0063-one-plugin-per-platform-for-sound-and-midi.md) | One plugin per platform, carrying its sound and its MIDI *(user-directed)* |
| [0102](0102-a-plugin-is-compiled-against-a-contract-with-a-version-of-its-own.md) | A plugin is compiled against a contract with a version of its own *(user-directed)* |
| [0132](0132-a-plugin-package-says-what-it-is-and-installs-only-when-asked.md) | A plugin package says what it is, and installs only when asked *(user-directed)* |
| [0134](0134-a-plugin-declares-its-modules-and-is-refused-for-one-it-did-not.md) | A plugin declares its modules, and is refused for one it did not *(user-directed)* |
| [0133](0133-a-shared-plugin-is-unpublished-until-the-admin-publishes-it.md) | A shared plugin is unpublished until the admin publishes it *(user-directed)* |
| [0135](0135-a-patch-that-names-a-plugin-you-do-not-have-offers-it.md) | A patch that names a plugin you do not have offers it *(user-directed)* |
| [0033](0033-patches-authored-by-an-agent-behind-the-plugin-boundary.md) | Patches may be authored by an agent, behind the plugin boundary *(user-directed; its all-or-nothing prose budget replaced by [0098](0098-the-briefing-has-a-budget-and-a-list-that-outranks-it.md))* |
| [0034](0034-settings-in-a-file-the-key-in-the-operating-system.md) | Settings in a file, the key in the operating system's store *(user-directed)* |
| [0088](0088-a-release-installs-itself-at-the-next-start.md) | A release installs itself at the next start, if its signature says it is ours *(user-directed)* |
| [0127](0127-the-person-chooses-what-opens-a-flyback-file.md) | The person chooses what opens a Flyback file *(user-directed)* |
| [0120](0120-every-change-passes-the-gate-a-release-passes.md) | Every change passes the gate a release passes |
| [0094](0094-a-run-says-what-it-played-and-nothing-about-who-played-it.md) | A run says what it played, and nothing about who played it *(user-directed; two more events and a wait at the end added by [0103](0103-a-run-says-how-it-ended-in-bands.md))* |
| [0103](0103-a-run-says-how-it-ended-in-bands.md) | A run says how it ended, in bands *(user-directed)* |
| [0047](0047-the-agent-may-listen-where-the-model-can.md) | The agent gets an ear, which is a second model *(user-directed)* |
| [0066](0066-a-second-wire-format-so-one-model-can-hear.md) | A second wire format, so one model can hear what it built *(user-directed)* |
| [0069](0069-an-assistant-declares-its-own-settings.md) | An assistant declares its own settings *(user-directed; its field vocabulary shared with every plugin by [0085](0085-a-sound-backend-declares-its-own-settings.md))* |
| [0085](0085-a-sound-backend-declares-its-own-settings.md) | A sound backend declares its own settings *(user-directed)* |
| [0072](0072-a-conversation-is-saved-with-the-patch-it-is-about.md) | A conversation is saved with the patch it is about *(user-directed)* |
| [0098](0098-the-briefing-has-a-budget-and-a-list-that-outranks-it.md) | The briefing has a budget, and a list that outranks it *(user-directed; its default raised to 100,000 by [0101](0101-a-one-knob-maximizer-is-a-module-of-its-own.md))* |
| [0113](0113-the-workbench-does-not-limit-how-large-a-patch-is.md) | The workbench does not limit how large a patch is *(user-directed)* |
| [0115](0115-the-assistant-may-read-the-presets.md) | The assistant may read the presets *(user-directed)* |
