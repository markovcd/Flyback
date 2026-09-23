using Flyback.Core.Compile;

namespace Flyback.Core.Graph;

public partial class NodeCatalog
{
    public const string CloudsTypeId = "pattern.clouds";

    private static IEnumerable<NodeDef> Patterns()
    {
        yield return new NodeDef(
            CloudsTypeId, "Clouds", ModuleCategories.Patterns,
            [..Position(), Num("z"), Num("scale", 2f, 0f, 32f)], [Num("out", 0f, 0f, 1f)],
            (em, i) => [em.Ternary(OpCode.Noise3, em.Mul(i[0], i[3]), em.Mul(i[1], i[3]), i[2])],
            "A smooth random field, 0 to 1: clouds, terrain, or a melody that wanders. "
            + "Drive z from Time to make it boil. For hiss or grain, use Noise.");

        yield return new NodeDef(
            "pattern.checker", "Checker", ModuleCategories.Patterns,
            [..Position(), Num("size", 4f, 0f, 32f)], [Num("out", 0f, 0f, 1f)],
            (em, i) =>
            {
                var fx = em.Unary(OpCode.Floor, em.Mul(i[0], i[2]));
                var fy = em.Unary(OpCode.Floor, em.Mul(i[1], i[2]));
                return [em.Mul(em.Unary(OpCode.Fract, em.Mul(em.Add(fx, fy), 0.5f)), 2f)];
            },
            "A chequerboard, 0 or 1.");

        yield return new NodeDef(
            "pattern.rings", "Rings", ModuleCategories.Patterns,
            [..Position(), Num("freq", 4f, 0f, 32f), Num("offset", 0f, 0f, 1f) with { Lenient = true }], [Num("out", 0f, -1f, 1f)],
            (em, i) =>
            {
                var radius = em.Binary(OpCode.Hypot, i[0], i[1]);
                return [em.Unary(OpCode.Sin, em.Mul(em.Add(em.Mul(radius, i[2]), i[3]), Tau))];
            },
            "Concentric sine rings. Drive offset from Time to pulse outward.");
    }
}