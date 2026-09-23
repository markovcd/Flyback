using System.Text;
using System.Text.Json;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using static Flyback.Plugins.Assist.ToolArguments;

namespace Flyback.Plugins.Assist;

/// <summary>
/// The two tools that answer by running the patch rather than by reading it:
/// <c>render</c> draws frames, and <c>listen</c> renders the sound and measures it.
/// </summary>
/// <remarks>
/// Kept apart because nothing here is about the graph: these two compile the patch,
/// run a renderer and turn what came back into something a model can be shown or
/// told. That is also what makes them the only asynchronous tools, and the only two
/// withheld from a model which cannot see or hear — see <see cref="Listener"/>.
/// </remarks>
public sealed partial class PatchWorkbench
{
    // --- rendering ----------------------------------------------------------

    /// <summary>
    /// Draws the patch and hands back a strip of frames.
    /// </summary>
    /// <remarks>
    /// On a pool thread rather than wherever the caller happened to be: an
    /// assistant's loop is consumed with <c>await foreach</c> on the dispatcher, so
    /// a render performed inline would land on the UI thread — which ADR-0018
    /// forbids and which deadlocks besides. Doing it here makes that impossible for
    /// a plugin to get wrong.
    /// <para>
    /// Frames are stepped from zero rather than jumped to, because the renderer owns
    /// the history <c>feedback</c> reads. Several frames rather than one, because a
    /// still cannot show motion.
    /// </para>
    /// </remarks>
    private Task<ToolOutcome> RenderAsync(JsonElement arguments, CancellationToken cancel)
    {
        var requested = Times(arguments);

        // Asked for directly, because the compiler does not remark on a color
        // socket left empty while the sound is wired — a patch built for the ear
        // is a deliberate thing, not a complaint waiting to happen. It is still
        // nothing to look at: what would come back is a black rectangle, and an
        // assistant shown black goes and "fixes" a patch that was working.
        if (working.IncomingTo(working.Output.Id, NodeCatalog.OutputColorPort) is null)
        {
            return Task.FromResult(ToolOutcome.Refused(
                "nothing is wired into the Output's 'color', so this patch draws nothing and "
                + "there is nothing to look at. Patch something in if it is meant to be seen."));
        }

        var patch = working.CompileForVideo(modules, samples, pictures);

        if (patch.HasIssues)
        {
            var why = patch.HasErrors
                ? "this patch does not compile, so there is nothing to look at: "
                : "there may be nothing to look at: ";

            return Task.FromResult(ToolOutcome.Refused(
                why + string.Join(" | ", patch.Issues.Select(i => i.Message))));
        }

        return Task.Run(
            () =>
            {
                var (width, height) = (limits.FrameWidth, limits.FrameHeight);
                var frameStride = width * 4;
                var sheetStride = frameStride * requested.Length;

                var frame = new byte[frameStride * height];
                var sheet = new byte[sheetStride * height];

                var capture = requested.Select(t => (int)Math.Round(t / limits.WarmUpStep)).ToArray();
                var renderer = new SynthRenderer();

                IlCompiler.CompileOnce(patch.Program, IlParts.Staged);

                for (var step = 0; step <= capture[^1]; step++)
                {
                    cancel.ThrowIfCancellationRequested();

                    renderer.Render(patch.Program, step * limits.WarmUpStep, width, height, frame, frameStride);

                    for (var i = 0; i < capture.Length; i++)
                    {
                        if (capture[i] != step) continue;

                        for (var y = 0; y < height; y++)
                        {
                            Array.Copy(
                                frame,
                                y * frameStride,
                                sheet,
                                y * sheetStride + i * frameStride,
                                frameStride);
                        }
                    }
                }

                var png = new MemoryStream();
                PngWriter.WriteBgra(png, sheet, width * requested.Length, height, sheetStride);

                var when = string.Join(", ", requested.Select(t => Number(t) + "s"));

                return ToolOutcome.Looked(
                    png.ToArray(),
                    $"{requested.Length} frames left to right at {when}, {width} by {height} each. "
                    + "The renderer was warmed from zero at thirty frames a second, so anything "
                    + "reading the previous frame shows the history it would really have.");
            },
            cancel);
    }

    private double[] Times(JsonElement arguments)
    {
        // Not from zero by default. A patch that reads the previous frame is
        // legitimately black on its first one, and an assistant shown a black
        // panel tends to go and "fix" a patch that was working.
        double[] fallback = [0.5d, 1.5d, 3.5d];

        if (!arguments.TryGetProperty("times", out var times) || times.ValueKind != JsonValueKind.Array)
            return fallback;

        var asked = times.EnumerateArray()
            .Where(t => t.ValueKind == JsonValueKind.Number)
            .Select(t => Math.Clamp(t.GetDouble(), 0d, limits.LatestTime))
            .Take(limits.MaxFrames)
            .Order()
            .ToArray();

        return asked.Length == 0 ? fallback : asked;
    }

    // --- listening ----------------------------------------------------------

    /// <summary>
    /// Renders a stretch of the patch's sound and hands it back as a WAV.
    /// </summary>
    /// <remarks>
    /// On a pool thread for the same reason <see cref="RenderAsync"/> is, though a
    /// couple of seconds of sound is cheaper than a single frame. What it is not
    /// cheap in is the request body it becomes, which is why
    /// <see cref="WorkbenchLimits.LongestListen"/> is short and
    /// <see cref="WorkbenchLimits.ListenRate"/> is half what the speakers use.
    /// <para>
    /// Warmed from zero rather than sought to: the audio path is the one with delay
    /// lines behind it (ADR-0027), so a patch started halfway along would be handed
    /// empty memory. Silence comes back as words rather than as a WAV — an
    /// oscillator whose <c>in</c> nothing drives is legal, compiles without a word
    /// and does not move, and a model played two seconds of nothing concludes the
    /// tool is broken.
    /// </para>
    /// </remarks>
    private Task<ToolOutcome> ListenAsync(JsonElement arguments, CancellationToken cancel)
    {
        // Asked of the graph rather than of the compiler, which is content with
        // an unwired sink: silence is a legal program, and what would come back
        // is a WAV full of zeroes rather than a complaint.
        if (!working.Reaches().Sound)
        {
            return Task.FromResult(ToolOutcome.Refused(
                "nothing is wired into the Output's 'left' or 'right', so this patch makes no "
                + "sound and there is nothing to hear. Patch something in if it is meant to be heard."));
        }

        var patch = working.CompileForAudio(modules, samples);

        if (patch.HasIssues)
        {
            var why = patch.HasErrors
                ? "this patch does not compile, so there is nothing to hear: "
                : "there may be nothing to hear: ";

            return Task.FromResult(ToolOutcome.Refused(
                why + string.Join(" | ", patch.Issues.Select(i => i.Message))));
        }

        var (from, seconds) = Window(arguments);

        return Task.Run(
            () =>
            {
                var renderer = new AudioRenderer(limits.ListenRate)
                {
                    Aspect = SynthRenderer.AspectOf(limits.FrameWidth, limits.FrameHeight),
                };

                IlCompiler.CompileOnce(patch.Program, IlParts.Whole);

                // Thrown away, but not skipped: this is the warm-up, and what it
                // leaves behind in the delay lines is the whole point of it.
                if (from > 0)
                {
                    var skipped = new float[Samples(from)];
                    renderer.Render(patch.Program, skipped);
                }

                cancel.ThrowIfCancellationRequested();

                var samples = new float[Samples(seconds)];
                renderer.Render(patch.Program, samples);

                var (peak, rms) = ClipLevels.Levels(samples);

                if (peak < ClipLevels.SilenceFloor)
                {
                    return ToolOutcome.Fine(
                        $"{Number(seconds)}s from {Number(from)}s is silence — nothing above "
                        + "-66 dBFS came out, so there is no point playing it to you. The "
                        + "compiler has already said whatever it can see, so look at what it "
                        + "cannot: 'volume' on the Output sitting at zero, or an 'in' that is wired "
                        + "but never moves — a knob, or anything else holding one value, drives a "
                        + "phase exactly as far as nothing does. Only a signal that changes with "
                        + "'t' makes an oscillator oscillate. A constant reaching 'left' is "
                        + "silent too: it is pure DC, and the DC blocker removes it.");
                }

                var wav = new MemoryStream();
                WavWriter.Write(wav, samples, renderer.SampleRate, NodeCatalog.AudioChannels);

                var caption = new StringBuilder(
                    $"{Number(seconds)}s of sound from {Number(from)}s, in stereo at "
                    + $"{limits.ListenRate / 1000} kHz. It was rendered from zero, so anything with "
                    + "a delay in it has the tail it would really have.");

                caption.Append("\n\n").Append(ClipLevels.Measured(samples, peak, rms));

                return ToolOutcome.Played(wav.ToArray(), caption.ToString());
            },
            cancel);
    }

    private int Samples(double seconds) =>
        (int)Math.Round(limits.ListenRate * seconds) * NodeCatalog.AudioChannels;

    /// <summary>Which stretch of the timeline to render, clamped to what one call may spend.</summary>
    private (double From, double Seconds) Window(JsonElement arguments)
    {
        var from = arguments.TryGetProperty("from", out var start) && start.ValueKind == JsonValueKind.Number
            ? Math.Clamp(start.GetDouble(), 0d, limits.LatestTime)
            : 0d;

        var seconds = arguments.TryGetProperty("seconds", out var length)
            && length.ValueKind == JsonValueKind.Number
                ? Math.Clamp(length.GetDouble(), 0.25d, limits.LongestListen)
                : Math.Min(2d, limits.LongestListen);

        return (from, seconds);
    }
}
