using Flyback.Core.Compile;

namespace Flyback.Core.Graph;

public partial class NodeCatalog
{
    private static IEnumerable<NodeDef> Patterns()
    {
        yield return new NodeDef(
            "pattern.noise", "Noise", ModuleCategories.Patterns,
            [..Position(), Num("z"), Num("scale", 2f, 0f, 32f)], [Num("out")],
            (em, i) => [em.Ternary(OpCode.Noise3, em.Mul(i[0], i[3]), em.Mul(i[1], i[3]), i[2])],
            "Smooth random field in 0..1. Drive z from Time to make it boil.");

        yield return new NodeDef(
            "pattern.checker", "Checker", ModuleCategories.Patterns,
            [..Position(), Num("size", 4f, 0f, 32f)], [Num("out")],
            (em, i) =>
            {
                var fx = em.Unary(OpCode.Floor, em.Mul(i[0], i[2]));
                var fy = em.Unary(OpCode.Floor, em.Mul(i[1], i[2]));
                return [em.Mul(em.Unary(OpCode.Fract, em.Mul(em.Add(fx, fy), 0.5f)), 2f)];
            },
            "A chequerboard, 0 or 1.");

        yield return new NodeDef(
            "pattern.rings", "Rings", ModuleCategories.Patterns,
            [..Position(), Num("freq", 4f, 0f, 32f), Num("offset")], [Num("out")],
            (em, i) =>
            {
                var radius = em.Binary(OpCode.Hypot, i[0], i[1]);
                return [em.Unary(OpCode.Sin, em.Mul(em.Add(em.Mul(radius, i[2]), i[3]), Tau))];
            },
            "Concentric sine rings. Drive offset from Time to pulse outward.");
    }
}