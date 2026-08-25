namespace Flyback.Core.Compile;

/// <summary>
/// One Scope, as the two programs of a patch see it between them: the speakers'
/// program writes into it and the screen's program reads out of it.
/// </summary>
/// <param name="Node">
/// Which module this belongs to. The two programs are compiled separately and
/// eliminate different dead code, so a Scope's position in one is nothing to do
/// with its position in the other — the id is what pairs them up.
/// </param>
/// <param name="Window">
/// How much of the past the chart is asking for, in seconds. Read off the knob
/// at compile time rather than out of a register, for the reason
/// <see cref="Flyback.Core.Render.AudioScan"/> reads the sweep off one: what
/// fills the buffer runs once a frame, outside the program, and a value that
/// arrives per sample is not something it could act on.
/// </param>
/// <param name="Trace">
/// The buffer itself — written by whoever is refilling it, read by the screen's
/// program as an ordinary <see cref="OpCode.Table"/>. Empty in the speakers'
/// program, which writes the ring rather than the buffer.
/// </param>
public sealed record TapSpec(Guid Node, float Window, LoadedSample Trace);

/// <summary>
/// The join between what the speakers played and what a Scope draws of it.
/// </summary>
/// <remarks>
/// Two things have to meet here and neither can reach the other on its own. The
/// audio path has the past — a ring per Scope in its <see cref="DelayState"/>,
/// written one evaluation at a time by <see cref="OpCode.Tap"/>. The video path
/// has the chart, which is an ordinary table read and knows nothing about sound.
/// Something running once a frame, on neither thread's schedule, copies the one
/// into the other; this is all of it.
/// <para>
/// Deliberately a copy rather than a shared buffer. The ring is written at 192
/// kHz by the sound callback and read at whatever a picture costs, and the
/// window a chart wants is a small slice of it, resampled — so there is a
/// transformation to do anyway, and doing it once a frame is cheaper than
/// teaching the drawing side about ring heads.
/// </para>
/// </remarks>
public static class Traces
{
    /// <summary>
    /// How many points a chart is drawn from. A preview is not often wider than
    /// this and never usefully so: the buffer is stretched across the window,
    /// which is a couple of thousand columns at most, and past that the eye is
    /// being shown interpolation rather than signal.
    /// </summary>
    public const int Points = 2048;

    /// <summary>
    /// A fresh buffer for one Scope, a second long by construction — the rate is
    /// the length, so <see cref="LoadedSample.Seconds"/> is one and a chart reads
    /// it at a position between nought and one whatever window it is showing.
    /// </summary>
    /// <remarks>
    /// That normalisation is what keeps the window knob out of the program. The
    /// buffer always holds exactly the stretch being shown, so the module's job
    /// is only to say how far across the picture it is — and the window can
    /// change without a recompile of anything but the refill.
    /// </remarks>
    public static LoadedSample Buffer() => new(new float[Points], Points);

    /// <summary>
    /// The stand-in a tap carries where there is no chart to fill: the speakers'
    /// program, which writes a ring rather than a buffer, and has no use for one
    /// at all.
    /// </summary>
    /// <remarks>
    /// Shared and empty rather than null, so that <see cref="TapSpec.Trace"/> is
    /// one thing everywhere and nobody pairing the two programs up has to ask
    /// which half they are holding.
    /// </remarks>
    public static LoadedSample Silence { get; } = new([], Points);

    /// <summary>
    /// Refills every Scope the screen is drawing from what the speakers have
    /// played since the last time this ran.
    /// </summary>
    /// <param name="drawn">The screen's program, whose taps carry the buffers.</param>
    /// <param name="heard">The speakers' program, whose taps say which ring is whose.</param>
    /// <param name="memory">
    /// The rings themselves, and null wherever there are none — a patch with no
    /// Scope in it, or one whose sound has never been switched on. Nothing is
    /// cleared in that case: a chart holds its last sweep rather than blanking,
    /// which is what a scope with the beam stopped looks like.
    /// </param>
    public static void Refresh(CompiledPatch drawn, CompiledPatch heard, DelayState? memory)
    {
        if (memory is null || drawn.Taps.Count == 0 || heard.Taps.Count == 0) return;

        for (var slot = 0; slot < heard.Taps.Count; slot++)
        {
            var played = heard.Taps[slot];

            foreach (var shown in drawn.Taps)
            {
                if (shown.Node != played.Node) continue;

                // The chart's window, in evaluations of the program that wrote
                // the ring. At least one, so a window turned to nothing is a
                // flat line rather than a division by nought, and no more than
                // the ring holds, so asking for a minute shows the two seconds
                // there are.
                var span = Math.Clamp(
                    (int)Math.Round(shown.Window * memory.SampleRate),
                    1,
                    DelayState.TraceSamples);

                memory.CopyTrace(slot, shown.Trace.Samples, span);
            }
        }
    }
}
