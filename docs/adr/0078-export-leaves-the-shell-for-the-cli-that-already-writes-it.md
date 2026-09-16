# ADR-0078: Export leaves the shell for the CLI that already writes it

**Status:** Accepted · 2026-09-16 · *user-directed* · removes the shell's half
of [0036](0036-export-video-as-motion-jpeg-in-an-avi.md) · amends
[0037](0037-one-output-block-that-every-patch-has.md)'s Output panel

## Context

A patch could be written to a PNG, a WAV or an AVI from two places: the
Output panel's `Export…` button, and `flyback-cli render`. The CLI command
predates nothing here — it takes the same patch, the same three extensions,
the same `MovieRenderer`/`PngWriter`/`WavWriter`, and the same default frame
size the shell's `ExportSize` used. Asking for a `.avi` gets Motion JPEG at
`MovieRenderer.DefaultFrameRate` either way. The two were never two features;
they were one export reachable from two doors.

The shell's door cost more to keep than the second doorway was worth: a
`length` control that existed for nothing else, an `exportButton` with its own
progress reporting and cancellation, the tip text explaining what the CLI's
`--help` already explains, and a panel row under "Export" that a `Record…`
button had to share.

Record stays. It answers a question the CLI cannot: what a live performance
did, GPU frame by frame, as knobs moved — [0049](0049-record-the-gpu-frame-not-the-interpreter.md).
Export answers a different, stateless question — what a patch, frozen, would
do for some length — and a process that starts, writes a file and exits is
already the right shape for that question. The shell does not need to be open
for it.

## Decision

**The Output panel keeps `Record…` and drops `Export…`, the seconds box
beside it, and the "Export" heading.** `flyback-cli render` is the only way to
write a deterministic clip, a still or a WAV from now on.

Removed from `MainWindow`: `exportButton`, `length`, the `export` cancellation
token, `MarkExportable`, `ExportAsync` and its three helpers, `ExportKinds`,
`ExportTip`/`NothingToExport`, `ExportAspect`, `ExportSeconds` and
`RenderAudioFile`. `MarkRecordable` is called directly where `MarkExportable`
used to be called for both.

`RecordKinds` keeps its own copy of the picture/sound guard — it was already
a near-duplicate of `ExportKinds` rather than built on it, so nothing there
changes.

## Consequences

**A still frame at the preview's current moment has no UI path any more.**
`ExportFrameAsync` rendered whatever the preview was showing, mid-feedback,
at a fixed size — something `flyback-cli render` cannot reproduce, since it
renders at `--at` seconds into a fresh evaluation rather than off a running
preview's history. Getting a PNG of a moment now means opening the file the
CLI wrote, or a frame pulled from a recording.

**Every other export is unchanged in what it produces**, only in how it is
asked for: `flyback-cli render patch.fbk out.avi --seconds 10` in place of
picking a length and pressing the button.

**The Output panel's write-a-file row is now just `Record…`.** A patch that
makes nothing still greys it out and says why, the same guard `MarkExportable`
used to run before it deferred to `MarkRecordable`.
