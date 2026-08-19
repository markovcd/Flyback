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

        yield return new NodeDef(
            "time", "Time", "Source",
            [Num("rate", 1f, 0f, 8f)], [Num("t")],
            (em, i) => [em.Mul(em.Load(OpCode.LoadT), i[0])],
            "Seconds since the patch started, scaled by rate.");

        yield return new NodeDef(
            "value", "Value", "Source",
            [Num("value", 0.5f)], [Num("out")],
            (_, i) => [i[0]],
            "A knob. Handy when several modules should share one number.");
    }
}