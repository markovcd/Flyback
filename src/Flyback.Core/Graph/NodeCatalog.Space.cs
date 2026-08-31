using Flyback.Core.Compile;

namespace Flyback.Core.Graph;

public partial class NodeCatalog
{
    private static IEnumerable<NodeDef> Space()
    {
        yield return new NodeDef(
            "space.rotate", "Rotate", ModuleCategories.Geometry,
            [..Position(), Num("angle", 0f, -Tau, Tau)], [Num("x"), Num("y")],
            (em, i) =>
            {
                var cos = em.Unary(OpCode.Cos, i[2]);
                var sin = em.Unary(OpCode.Sin, i[2]);
                return
                [
                    em.Binary(OpCode.Sub, em.Mul(i[0], cos), em.Mul(i[1], sin)),
                    em.Binary(OpCode.Add, em.Mul(i[0], sin), em.Mul(i[1], cos)),
                ];
            },
            "Spins the coordinate system. Feed the angle from an oscillator to make it turn.");

        yield return new NodeDef(
            "space.scale", "Scale", ModuleCategories.Geometry,
            [..Position(), Num("scale", 1f, 0f, 16f)], [Num("x"), Num("y")],
            (em, i) => [em.Mul(i[0], i[2]), em.Mul(i[1], i[2])],
            "Zooms the coordinate system. Larger scale packs more pattern in.");

        yield return new NodeDef(
            "space.translate", "Translate", ModuleCategories.Geometry,
            [..Position(), Num("dx"), Num("dy")], [Num("x"), Num("y")],
            (em, i) => [em.Binary(OpCode.Sub, i[0], i[2]), em.Binary(OpCode.Sub, i[1], i[3])],
            "Slides the coordinate system, moving the pattern by (dx, dy).");

        yield return new NodeDef(
            "space.polar", "To polar", ModuleCategories.Geometry,
            [..Position()], [Num("radius"), Num("angle")],
            (em, i) => [em.Binary(OpCode.Hypot, i[0], i[1]), em.Binary(OpCode.Atan2, i[1], i[0])],
            "Cartesian to polar. Patterns built on radius and angle go circular.");

        yield return new NodeDef(
            "space.tile", "Tile", ModuleCategories.Geometry,
            [..Position(), Num("tiles", 3f, 1f, 16f)], [Num("x"), Num("y")],
            (em, i) =>
            {
                return [Cell(i[0]), Cell(i[1])];

                Slot Cell(Slot v) =>
                    em.Add(em.Mul(em.Unary(OpCode.Fract, em.Add(em.Mul(em.Mul(v, i[2]), 0.5f), 0.5f)), 2f), -1f);
            },
            "Repeats the coordinate system into a grid of identical cells.");

        yield return new NodeDef(
            "space.mirror", "Mirror", ModuleCategories.Geometry,
            [..Position()], [Num("x"), Num("y")],
            (em, i) => [em.Unary(OpCode.Abs, i[0]), em.Unary(OpCode.Abs, i[1])],
            "Folds each axis about zero, so one quadrant is reflected into all four.");

        yield return new NodeDef(
            "space.kaleidoscope", "Kaleidoscope", ModuleCategories.Geometry,
            [..Position(), Num("segments", 6f, 1f, 24f)], [Num("x"), Num("y")],
            (em, i) =>
            {
                var radius = em.Binary(OpCode.Hypot, i[0], i[1]);
                var angle = em.Binary(OpCode.Atan2, i[1], i[0]);
                var segment = em.Binary(OpCode.Div, em.Constant(Tau), i[2]);
                var half = em.Mul(segment, 0.5f);
                var folded = em.Unary(OpCode.Abs,
                    em.Binary(OpCode.Sub, em.Binary(OpCode.Mod, angle, segment), half));
                return
                [
                    em.Mul(em.Unary(OpCode.Cos, folded), radius),
                    em.Mul(em.Unary(OpCode.Sin, folded), radius),
                ];
            },
            "Folds the plane into wedges around the centre.");

        yield return new NodeDef(
            "space.warp", "Warp", ModuleCategories.Geometry,
            [..Position(), Num("by"), Num("amount", 0.5f)], [Num("x"), Num("y")],
            (em, i) =>
            {
                var push = em.Mul(i[2], i[3]);
                return
                [
                    em.Binary(OpCode.Add, i[0], push),
                    em.Binary(OpCode.Add, i[1], em.Unary(OpCode.Sin, em.Mul(push, Tau))),
                ];
            },
            "Displaces coordinates by another signal. This is where patches stop looking geometric.");
    }
}