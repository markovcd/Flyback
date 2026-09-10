namespace Flyback.Core.Compile;

/// <summary>
/// The join between what the speakers played and what the picture is allowed to
/// know about it: one number per Meter, per frame.
/// </summary>
/// <remarks>
/// <see cref="Traces"/>'s opposite. That carries a stretch of the past across as a
/// buffer, read with <see cref="OpCode.Table"/>, which the shader cannot draw;
/// this carries the same stretch across as its loudness, which is played in the
/// way a key is — through <see cref="LiveValues"/> and out as
/// <see cref="OpCode.LoadLive"/>, a uniform on the shader and free.
/// <para>
/// So the picture does not work out how loud the sound is, and could not: a frame
/// is one evaluation per pixel with no past to reduce. Something outside both
/// programs listens to the ring the speakers are filling and plays the answer in.
/// </para>
/// <para>
/// Once a frame, on whichever thread the frame is drawn from: a picture cannot
/// show a level it was told about between two of its own frames.
/// </para>
/// </remarks>
public static class Meters
{
    /// <summary>
    /// What a Meter listens on, before the signal it wants. The prefix is a word
    /// no instrument can take, so a meter and a keyboard can never collide in one
    /// block — see <see cref="LiveValues"/>, which is keyed by name for this
    /// reason.
    /// </summary>
    private const string Prefix = "meter";

    /// <summary>The loudness of the window, which is what a level meter shows.</summary>
    public const string Level = "level";

    /// <summary>The furthest the window got from nought, which is what hits.</summary>
    public const string Peak = "peak";

    /// <summary>
    /// The name one Meter's reading is played on. Built from the node id because
    /// the two programs of a patch share no numbering and this is the only thing
    /// they do share — the same problem <see cref="TapSpec.Node"/> answers the
    /// same way.
    /// </summary>
    public static string Key(Guid node, string signal) => $"{Prefix}/{node:N}/{signal}";

    /// <summary>
    /// Measures every Meter the picture is listening to and plays the answer into
    /// <paramref name="blocks"/>.
    /// </summary>
    /// <param name="heard">
    /// The speakers' program, whose taps say which ring belongs to which node.
    /// </param>
    /// <param name="memory">
    /// The rings themselves, and null where there are none. Everything reads nought
    /// there, which is deliberately not what <see cref="Traces.Refresh"/> does with
    /// the same absence: a chart holding its last sweep reads as a stopped beam,
    /// where a frozen level is a lit picture with nothing playing.
    /// </param>
    /// <param name="blocks">
    /// Every block that might be listening — the screen's, and the speakers' own
    /// where the sound is driven by its own level. Written by name, so the two are
    /// allowed to disagree about which meters exist while a recompile is in flight.
    /// </param>
    public static void Refresh(CompiledPatch heard, DelayState? memory, params LiveValues[] blocks)
    {
        ArgumentNullException.ThrowIfNull(heard);
        ArgumentNullException.ThrowIfNull(blocks);

        if (blocks.Length == 0 || heard.Taps.Count == 0) return;

        for (var slot = 0; slot < heard.Taps.Count; slot++)
        {
            var tap = heard.Taps[slot];

            var level = Key(tap.Node, Level);
            var peak = Key(tap.Node, Peak);

            // Every tap is offered, and a Scope's is refused here by nobody
            // reading it. That is cheaper than knowing which kind of module each
            // ring belongs to, and it is the same question either way: a name a
            // program does not read is a measurement nothing would look at, and
            // the window behind it is a few hundred thousand samples to walk.
            if (!Wanted(blocks, level, peak)) continue;

            var span = memory is null
                ? 0
                : Math.Clamp((int)Math.Round(tap.Window * memory.SampleRate), 1, DelayState.TraceSamples);

            var (loudest, loudness) = memory is null ? (0f, 0f) : memory.Measure(slot, span);

            Play(blocks, level, loudness);
            Play(blocks, peak, loudest);
        }
    }

    /// <summary>
    /// Every Meter back to nothing, for when the sound stops. The programs are
    /// still loaded and the picture is still being drawn, so the readings have to
    /// be put out rather than merely left — see the note on <c>memory</c> above.
    /// </summary>
    public static void Silence(CompiledPatch heard, params LiveValues[] blocks)
    {
        ArgumentNullException.ThrowIfNull(heard);

        Refresh(heard, null, blocks);
    }

    private static bool Wanted(LiveValues[] blocks, string level, string peak)
    {
        foreach (var block in blocks)
            if (block.Reads(level) || block.Reads(peak))
                return true;

        return false;
    }

    private static void Play(LiveValues[] blocks, string key, float value)
    {
        foreach (var block in blocks) block.Set(key, value);
    }
}
