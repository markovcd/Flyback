using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Picture;

/// <summary>
/// The distance to a rectangle, with corners that can be rounded off.
/// </summary>
/// <remarks>
/// The standard construction: fold the plane into one quadrant with two absolutes
/// and take the box's corner as the origin. Outside, the distance is the length of
/// the part that went past, each axis held at zero where it did not; inside, both
/// are negative and the answer is the larger, being the nearest wall. The two
/// cases are added rather than chosen between, because only one is ever non-zero.
/// <para>
/// Rounding is one subtraction: a field grown outward by r is the same field minus
/// r, so the box is built r smaller and grown back. The radius is held to the
/// shorter half-side, past which no straight edge is left to round.
/// </para>
/// </remarks>
internal static class BoxModule
{
    public const string TypeId = "flyback.picture.box";

    public static NodeDef Definition { get; } = new(
        TypeId, "Box", ModuleCategories.Forms,
        [
            ..Field.Position(),
            Field.Size("width", 0.5f),
            Field.Size("height", 0.5f),
            Field.Size("corner", 0f, 1f),
        ],
        [Field.Distance("distance")],
        Emit,
        "A rectangle, as a distance. 'width' and 'height' are half-sizes, so they read as how "
        + "far it reaches from the middle — the same way a radius does, and the reason a box "
        + "and a circle of the same number are the same size. 'corner' rounds the corners off, "
        + "up to the point where the shape is a capsule and then a disc.");

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var zero = em.Constant(0f);

        var round = em.Ternary(
            OpCode.Clamp, node[4], zero, em.Binary(OpCode.Min, node[2], node[3]));

        var q = new[]
        {
            em.Sub(em.Unary(OpCode.Abs, node[0]), em.Sub(node[2], round)),
            em.Sub(em.Unary(OpCode.Abs, node[1]), em.Sub(node[3], round)),
        };

        var outside = em.Binary(
            OpCode.Hypot,
            em.Binary(OpCode.Max, q[0], zero),
            em.Binary(OpCode.Max, q[1], zero));

        var inside = em.Binary(OpCode.Min, em.Binary(OpCode.Max, q[0], q[1]), zero);

        return [em.Sub(em.Add(outside, inside), round)];
    }
}
