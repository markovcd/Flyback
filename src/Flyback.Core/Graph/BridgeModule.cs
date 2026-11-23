using Flyback.Core.Compile;
using static Flyback.Core.Graph.Sockets;

namespace Flyback.Core.Graph;

/// <summary>The Bridge: a value the sound works out, read by the picture once a frame.</summary>
internal static class BridgeModule
{
    /// <summary>Named here because what it reads is filled in from outside the program — see <c>Compile.Meters</c>.</summary>
    internal const string TypeId = "bridge";

    /// <summary>The input whose knob says where the value wraps, read at compile time.</summary>
    internal const int WrapPort = 2;

    /// <summary>The longest a wrap may be: past this a float can no longer tell one step of the circle from the next.</summary>
    internal const float MaxWrap = 65536f;

    /// <summary>
    /// A value the sound works out, read by the picture once a frame.
    /// </summary>
    /// <remarks>
    /// A Meter's plumbing with a different reading: the mean of the window rather
    /// than its loudness, so the sign and the level survive. With 'wrap' set the
    /// mean is taken round a circle, so a phase or a count crossing its top reads
    /// as crossing it rather than as a smear back through the middle. Both knobs
    /// are read at compile time, because what fills the reading in runs outside
    /// the program.
    /// </remarks>
    internal static NodeDef Def() =>
        new NodeDef(
            TypeId, "Bridge", ModuleCategories.Measurement,
            [
                Swept("in") with
                {
                    Help = "The sound-side value to carry over: an envelope, a slew, a count, a phase.",
                },
                Seconds("window", -1.7f) with
                {
                    Help = "How far back the reading averages. About a frame (20 ms) is smooth; "
                        + "shorter follows faster and may step.",
                },
                Num("wrap", 0f, 0f, MaxWrap) with
                {
                    // Even up to a phase's 1, then decades for the counts.
                    Knee = 1f,
                    Help = "0 for a plain value. Otherwise the value wraps at this, like a phase at 1 or a count "
                        + "at 256, and 'value' reads between 0 and this without sweeping back at the wrap.",
                },
            ],
            [
                Num("value") with { Help = "The input averaged over 'window', round the circle when 'wrap' is set." },
                Num("top") with
                {
                    Help = "The highest the input got over 'window', so a trigger one evaluation long is seen "
                        + "as long as the window is at least a frame.",
                },
            ],
            (em, node) =>
            {
                // Nought where there is no instance to be addressed as: nothing
                // outside could fill in a reading for a node not in the patch.
                if (node.Node == Guid.Empty) return [em.Constant(0f), em.Constant(0f)];

                return
                [
                    em.Live(MeterSignals.Key(node.Node, MeterSignals.Value)),
                    em.Live(MeterSignals.Key(node.Node, MeterSignals.Top)),
                ];
            },
            "Carries a value the sound works out (an envelope, a slew, a hold, a counter, a phase) into the "
            + "picture, once a frame. A stateful module read by the picture directly gets something simpler, "
            + "so put a Bridge between them. Set 'wrap' for a phase or a count that wraps. Costs the picture "
            + "nothing and keeps the GPU. Reads nothing where no sound runs, as in an exported still.")
        {
            Words = "sound value to picture",
            TapsSignal = true,
            Sinks = ModuleSinks.Video,
        };
}
