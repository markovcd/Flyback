using Flyback.Core.Compile;

namespace Flyback.Core.Graph;

public partial class NodeCatalog
{
    private static IEnumerable<NodeDef> Sources()
    {
        yield return new NodeDef(
            "coord", "Coordinates", "Source",
            [], [Num("x"), Num("y"), Num("radius"), Num("angle")],
            (em, _) =>
            {
                var x = em.Load(OpCode.LoadX);
                var y = em.Load(OpCode.LoadY);
                return
                [
                    x,
                    y,
                    em.Binary(OpCode.Hypot, x, y),
                    em.Binary(OpCode.Atan2, y, x),
                ];
            },
            "Where you are on screen. y runs -1..1, x is widened by the aspect ratio.");

        // No rate knob, and that is the decision rather than an omission — see
        // ADR-0048. It was a second, hidden speed control: a Time at 0.2 feeding
        // an oscillator divides its pitch by five while the freq knob goes on
        // saying otherwise, and nothing about the patch shows where the fifth
        // went. Multiply is how you scale a signal here, as it is for every
        // other signal in the catalogue.
        yield return new NodeDef(
            "time", "Time", "Source",
            [], [Num("t")],
            (em, _) => [em.Load(OpCode.LoadT)],
            "Seconds since the patch started. To run something slower, put a Multiply after "
            + "this — 0.2 for a fifth of the speed. Into an oscillator's 'in' it needs no "
            + "scaling at all: that is what the oscillator's 'freq' is.");

        yield return new NodeDef(
            "value", "Value", "Source",
            [Num("value", 0.5f)], [Num("out")],
            (_, i) => [i[0]],
            "A knob. Handy when several modules should share one number.");
    }
}