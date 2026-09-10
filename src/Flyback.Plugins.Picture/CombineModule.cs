using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Picture;

/// <summary>
/// Two shapes made into one: joined, overlapped or cut away, with the seam between
/// them as soft as it is asked to be.
/// </summary>
/// <remarks>
/// The hard versions are already in the catalogue — union is Minimum, intersection
/// is Maximum, difference is the maximum of a and the negative of b — which is the
/// argument for the distance convention rather than a fact about this module.
/// <para>
/// What this adds is the seam. A minimum has a crease wherever its arguments
/// cross, so two blobs meeting under one look like two blobs overlapping; the
/// polynomial smooth minimum replaces the crossing with a short quadratic blend,
/// and what comes out still measures roughly what it did. All three are one blend
/// read three ways, so intersection costs two ops on top of union; difference has
/// to be worked out again, blending a against a shape turned inside out.
/// </para>
/// <para>
/// At a smoothness of nothing all three are exactly the hard versions. The knob is
/// held a hair above zero, because the blend divides by its own width and a Divide
/// by nothing is nothing here — which would average the two shapes rather than
/// choose between them.
/// </para>
/// </remarks>
internal static class CombineModule
{
    public const string TypeId = "flyback.picture.combine";

    /// <summary>
    /// The narrowest seam the blend is evaluated at. Far below a pixel at any
    /// size the picture is ever drawn, so a knob at zero is a hard edge as far as
    /// anything can see, and the arithmetic still has a width to divide by.
    /// </summary>
    private const float Narrowest = 1e-4f;

    public static NodeDef Definition { get; } = new(
        TypeId, "Combine", ModuleCategories.Forms,
        [
            Field.Distance("a"),
            Field.Distance("b"),
            Field.Size("smoothness", 0f, 1f),
        ],
        [
            Field.Distance("union"),
            Field.Distance("intersection"),
            Field.Distance("difference"),
        ],
        Emit,
        "Two shapes into one, three ways at once: 'union' is both of them, 'intersection' is "
        + "only where they overlap, and 'difference' is a with b cut out of it. 'smoothness' "
        + "melts the seam where they meet — at 0 the corners are sharp and this is exactly a "
        + "Minimum and a Maximum, and turned up the two forms flow into each other. Chain "
        + "them for more than two shapes; the outputs are distances like the inputs, so "
        + "anything here can be combined again, filled or outlined.");

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var zero = em.Constant(0f);
        var one = em.Constant(1f);

        var a = node[0];
        var b = node[1];
        var seam = em.Binary(OpCode.Max, node[2], em.Constant(Narrowest));

        var (union, intersection) = Blend(a, b);

        // Cutting b away is intersecting with everything b is not, and a shape
        // turned inside out is its distance negated.
        var (_, difference) = Blend(a, em.Unary(OpCode.Neg, b));

        return [union, intersection, difference];

        // The two halves of one crossing: how far through the blend this point
        // is, and how deep the blend dips at that point. Their sum is the smooth
        // maximum and their difference the smooth minimum, which is why both come
        // back from one call.
        (Slot Lower, Slot Upper) Blend(Slot first, Slot second)
        {
            var through = em.Ternary(
                OpCode.Clamp,
                em.Add(
                    em.Mul(em.Binary(OpCode.Div, em.Sub(second, first), seam), 0.5f),
                    0.5f),
                zero,
                one);

            var dip = em.Mul(em.Mul(seam, through), em.Sub(one, through));

            return
            (
                em.Sub(em.Ternary(OpCode.Mix, second, first, through), dip),
                em.Add(em.Ternary(OpCode.Mix, second, first, em.Sub(one, through)), dip)
            );
        }
    }
}
