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

        yield return Transform();

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

    /// <summary>What a Transform's settings are filed under, and the one it has.</summary>
    public const string TransformStateKey = "transform";

    public const string TransformOrderKey = "order";

    /// <summary>The order that is not the default: Rotate first, then Scale.</summary>
    public const string TurnThenZoom = "turn";

    private const string ZoomThenTurn = "zoom";

    /// <summary>
    /// Scale, Rotate and Translate in one module, which is how a plane is nearly
    /// always placed: a picture is zoomed and turned before anything is drawn on it.
    /// </summary>
    /// <remarks>
    /// Their arithmetic, and the knob names <see cref="Trails"/> has for the same
    /// three. The order of the first two is a setting rather than a decision,
    /// because patches make both and the two are not the same to the last bit: a
    /// product taken before the sine and cosine rounds differently from one taken
    /// after. Translate is last either way, and at rest — less nought — is exact,
    /// as the other two are.
    /// </remarks>
    private static NodeDef Transform() => new(
        "space.transform", "Transform", ModuleCategories.Geometry,
        [..Position(), Num("zoom", 1f, 0f, 16f), Num("angle", 0f, -Tau, Tau), Num("dx"), Num("dy")],
        [Num("x"), Num("y")],
        (em, i) =>
        {
            var turnFirst = i.Extra<ExtraState>(TransformStateKey)?.Chosen(TransformOrderKey) == TurnThenZoom;

            var x = i[0];
            var y = i[1];

            if (!turnFirst) (x, y) = (em.Mul(x, i[2]), em.Mul(y, i[2]));

            var cos = em.Unary(OpCode.Cos, i[3]);
            var sin = em.Unary(OpCode.Sin, i[3]);

            (x, y) = (
                em.Binary(OpCode.Sub, em.Mul(x, cos), em.Mul(y, sin)),
                em.Binary(OpCode.Add, em.Mul(x, sin), em.Mul(y, cos)));

            if (turnFirst) (x, y) = (em.Mul(x, i[2]), em.Mul(y, i[2]));

            return [em.Binary(OpCode.Sub, x, i[4]), em.Binary(OpCode.Sub, y, i[5])];
        },
        "Zooms, turns and slides the coordinate system: a Scale, a Rotate and a Translate in "
        + "one. 'angle' is in radians, and 'dx' and 'dy' move the pattern. Whether it zooms or "
        + "turns first is set on the node.")
    {
        Extras =
        [
            new SettingsExtra(
                TransformStateKey,
                [
                    new ExtraField.Choice(
                        TransformOrderKey,
                        "order",
                        [new ChoiceOption(ZoomThenTurn, "Zoom, then turn"), new ChoiceOption(TurnThenZoom, "Turn, then zoom")],
                        ZoomThenTurn),
                ]),
        ],
    };
}