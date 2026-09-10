namespace Flyback.Core.Compile;

/// <summary>
/// One Scope, as the two programs of a patch see it between them: the speakers'
/// program writes into it and the screen's program reads out of it.
/// </summary>
/// <param name="Node">
/// Which module this belongs to. The two programs eliminate different dead code,
/// so position in one says nothing about position in the other — the id pairs
/// them up.
/// </param>
/// <param name="Window">
/// How much of the past the chart is asking for, in seconds. Read off the knob at
/// compile time, because what fills the buffer runs once a frame and outside the
/// program.
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
/// The audio path has the past — a ring per Scope in its
/// <see cref="DelayState"/>, written by <see cref="OpCode.Tap"/> — and the video
/// path has the chart, an ordinary table read that knows nothing about sound.
/// Something running once a frame copies one into the other; this is all of it.
/// A copy rather than a shared buffer, because the window a chart wants is a
/// small slice of the ring, resampled: there is a transformation to do anyway.
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
    /// the length, so a chart reads it between nought and one whatever window it
    /// shows.
    /// </summary>
    /// <remarks>
    /// That normalisation keeps the window knob out of the program: the buffer
    /// always holds exactly the stretch being shown, so the module only says how
    /// far across the picture it is.
    /// </remarks>
    public static LoadedSample Buffer() => new(new float[Points], Points);

    /// <summary>
    /// The stand-in a tap carries where there is no chart to fill: the speakers'
    /// program, which writes a ring rather than a buffer. Shared and empty rather
    /// than null, so <see cref="TapSpec.Trace"/> is one thing everywhere.
    /// </summary>
    public static LoadedSample Silence { get; } = new([], Points);

    /// <summary>
    /// Refills every Scope the screen is drawing from what the speakers have
    /// played since the last time this ran.
    /// </summary>
    /// <param name="drawn">The screen's program, whose taps carry the buffers.</param>
    /// <param name="heard">The speakers' program, whose taps say which ring is whose.</param>
    /// <param name="memory">
    /// The rings themselves, and null wherever there are none. Nothing is cleared
    /// in that case: a chart holds its last sweep, which is what a scope with the
    /// beam stopped looks like.
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
                // the ring holds — which is now sized to the knob's own ceiling,
                // so this clamp is headroom rather than a second, smaller cap
                // nobody sees.
                var span = Math.Clamp(
                    (int)Math.Round(shown.Window * memory.SampleRate),
                    1,
                    DelayState.TraceSamples);

                memory.CopyTrace(slot, shown.Trace.Samples, span);
            }
        }
    }
}
