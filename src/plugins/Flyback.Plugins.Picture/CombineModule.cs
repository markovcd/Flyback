using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Picture;

/// <summary>
/// Two shapes made into one: joined, overlapped or cut away, with the seam between
/// them as soft as it is asked to be.
/// </summary>
/// <remarks>
/// The hard versions are Minimum, Maximum, and the maximum of a and -b; this adds
/// the polynomial smooth minimum's quadratic blend at the seam. Intersection
/// reuses union's blend; difference blends a against an inverted b. Smoothness is
/// held a hair above zero, since Divide by zero gives zero here and would average
/// the shapes.
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
            Field.Distance("a") with { Help = "One shape's distance." },
            Field.Distance("b") with { Help = "The other shape's distance." },
            Field.Size("smoothness", 0f, 1f) with
            {
                Help = "Melts the seam. At 0 it is exactly a Minimum and a Maximum.",
            },
        ],
        [
            Field.Distance("union") with { Help = "Both shapes." },
            Field.Distance("intersection") with { Help = "Only the overlap." },
            Field.Distance("difference") with { Help = "'a' with 'b' cut out." },
        ],
        Emit,
        "Two shapes into one, three ways. The outputs are distances, so they chain, fill or "
        + "outline like any shape.")
    {
        Skin = new ModuleSkin.Palette(CategoryAccents.Of(ModuleCategories.Forms))
        {
            Glyph = "M9,7 A6,6 0 1 0 9,19 A6,6 0 1 0 9,7 M15,7 A6,6 0 1 0 15,19 A6,6 0 1 0 15,7",
        },
    };

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
