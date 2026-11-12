using Flyback.Core.Compile;
using Flyback.Core.Graph.Extras;
using static Flyback.Core.Graph.Sockets;

namespace Flyback.Core.Graph;

/// <summary>The Meter: how loud the speakers are, as a number the picture can use.</summary>
internal static class MeterModule
{
    /// <summary>
    /// The level meter. Named here because what it reads is filled in from
    /// outside the program — see <c>Compile.Meters</c>.
    /// </summary>
    internal const string TypeId = "meter";

    /// <summary>
    /// How loud the speakers are, as a number the picture can use.
    /// </summary>
    /// <remarks>
    /// No opcode and no arithmetic: two <see cref="OpCode.LoadLive"/>s and a
    /// divide, filled in once a frame by <c>Meters</c> from the ring the
    /// Scope charts — so this survives to the shader, where a Scope cannot. The
    /// input is tapped and <see cref="PortSpec.Swept"/>, so a picture driven by a
    /// bass line reads one number rather than computing the line per pixel.
    /// 'window' is read off the knob at compile time, since what fills the answer
    /// in runs outside the program; 'scale' is applied after and may be swept.
    /// </remarks>
    internal static NodeDef Def() =>
        new NodeDef(
            TypeId, "Meter", ModuleCategories.Measurement,
            [
                Swept("in") with { Help = "Patch the Output's 'left' here for everything, one voice for just that." },
                Seconds("window", -1.3f) with
                {
                    Help = "How far back both readings look. A few milliseconds follows every drum; "
                        + "half a second leans into the music.",
                },
                Num("scale", 1f, 0.01f, 16f) with { Help = "Divides both readings." },
            ],
            [
                Num("level") with { Help = "The loudness over 'window', about 1 at full scale." },
                Num("peak") with { Help = "The furthest from silence, about 1 at full scale. The one that hits." },
            ],
            (em, node) =>
            {
                // Nought where there is no instance to be addressed as — the
                // hidden module a normalled socket reads. Nothing outside could
                // fill in a reading for a node that is not in the patch.
                if (node.Node == Guid.Empty) return [em.Constant(0f), em.Constant(0f)];

                var scale = em.Binary(OpCode.Max, node[2], em.Constant(0.01f));

                return
                [
                    em.Binary(OpCode.Div, em.Live(MeterSignals.Key(node.Node, MeterSignals.Level)), scale),
                    em.Binary(OpCode.Div, em.Live(MeterSignals.Key(node.Node, MeterSignals.Peak)), scale),
                ];
            },
            "How loud the sound is, as a number to draw with: patch a signal into 'in' and drive a "
            + "hue, a size or a brightness from 'level' or 'peak'. Costs the picture nothing and "
            + "keeps the GPU. Reads nothing where no sound runs, as in an exported still.")
        {
            Words = "loudness as a number",
            // Tapped but not charted: what it wants from the ring is two numbers
            // rather than a buffer, so no chart is allocated for it and nothing
            // refills one. See NodeDef.ChartsSignal.
            TapsSignal = true,
            Sinks = ModuleSinks.Video,
        };
}
