using Flyback.Core.Compile;

namespace Flyback.Core.Graph;

public partial class NodeCatalog
{
    public const string CloudsTypeId = "pattern.clouds";

    private static IEnumerable<NodeDef> Patterns()
    {
        yield return new NodeDef(
            CloudsTypeId, "Clouds", ModuleCategories.Patterns,
            [
                ..Position(),
                Num("z") with { Help = "A third axis through the field. Drive it from Time to make it boil." },
                Num("scale", 2f, 0f, 32f) with { Help = "Multiplies 'x' and 'y'. Larger is finer detail." },
            ],
            [Num("out", 0f, 0f, 1f) with { Help = "The field, 0 to 1." }],
            (em, i) => [em.Ternary(OpCode.Noise3, em.Mul(i[0], i[3]), em.Mul(i[1], i[3]), i[2])],
            "A smooth random field, 0 to 1: clouds, terrain, or a melody that wanders. "
            + "For hiss or grain, use Noise.");

        yield return new NodeDef(
            "pattern.checker", "Checker", ModuleCategories.Patterns,
            [..Position(), Num("size", 4f, 0f, 32f) with { Help = "Squares to each unit of 'x' and 'y'." }],
            [Num("out", 0f, 0f, 1f) with { Help = "1 on one color of square, 0 on the other." }],
            (em, i) =>
            {
                var fx = em.Unary(OpCode.Floor, em.Mul(i[0], i[2]));
                var fy = em.Unary(OpCode.Floor, em.Mul(i[1], i[2]));
                return [em.Mul(em.Unary(OpCode.Fract, em.Mul(em.Add(fx, fy), 0.5f)), 2f)];
            },
            "A checkerboard, 0 or 1.");

        yield return new NodeDef(
            "pattern.rings", "Rings", ModuleCategories.Patterns,
            [
                ..Position(),
                Num("freq", 4f, 0f, 32f) with { Help = "Rings to each unit of distance from the center." },
                Num("offset", 0f, 0f, 1f) with { Lenient = true, Help = "Shifts the rings, one whole ring at 1. Drive it from Time to make them pulse." },
            ],
            [Num("out", 0f, -1f, 1f) with { Help = "The rings, a sine -1 to 1 out from the center." }],
            (em, i) =>
            {
                var radius = em.Binary(OpCode.Hypot, i[0], i[1]);
                return [em.Unary(OpCode.Sin, em.Mul(em.Add(em.Mul(radius, i[2]), i[3]), Tau))];
            },
            "Concentric sine rings round the center.");
    }
}