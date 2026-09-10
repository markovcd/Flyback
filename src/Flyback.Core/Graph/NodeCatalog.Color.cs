using Flyback.Core.Compile;

namespace Flyback.Core.Graph;

public partial class NodeCatalog
{
    private static IEnumerable<NodeDef> Color()
    {
        yield return new NodeDef(
            "color.rgb", "RGB", ModuleCategories.Color,
            // White, so a fresh one is a color rather than the absence of one.
            // HSV arrives at full brightness with a hue already picked; this
            // arrives at full brightness with no channel picked, which is white
            // — and pulling any knob down from there tints it, which is the
            // module demonstrating itself. Black would have been the tidier
            // number and is the one default that draws nothing at all.
            [Num("r", 1f, 0f, 1f), Num("g", 1f, 0f, 1f), Num("b", 1f, 0f, 1f)], [Col("color")],
            (em, i) => [em.Combine(i[0], i[1], i[2])],
            "Builds a color from three separate signals. It starts white — every channel full — "
            + "so turning one down tints it and patching a signal into one drives that channel "
            + "against the other two.");

        yield return new NodeDef(
            "color.hsv", "HSV", ModuleCategories.Color,
            [Num("hue", 0f, 0f, 1f), Num("saturation", 1f, 0f, 1f), Num("value", 1f, 0f, 1f)], [Col("color")],
            (em, i) => [em.Triple(OpCode.HsvToRgb, i[0], i[1], i[2])],
            "Hue, saturation, value. Sweeping hue is the fastest route to rainbows.");

        yield return new NodeDef(
            "color.split", "Split", ModuleCategories.Color,
            [Col("color")], [Num("r"), Num("g"), Num("b")],
            (_, i) => [Slot.Scalar(i[0].Base), Slot.Scalar(i[0].Base + 1), Slot.Scalar(i[0].Base + 2)],
            "Pulls a color apart into its three channels.");

        yield return new NodeDef(
            "color.mix", "Blend", ModuleCategories.Color,
            [Col("a"), Col("b"), Num("t", 0.5f, 0f, 1f)], [Col("color")],
            (em, i) => [em.Ternary(OpCode.Mix, i[0], i[1], i[2])],
            "Crossfades between two colors.");

        yield return new NodeDef(
            "color.gain", "Gain", ModuleCategories.Color,
            [Col("color"), Any("gain", 1f, 0f), Any("bias", 0f, -1f, 1f)], [Col("color")],
            (em, i) => [em.Binary(OpCode.Add, em.Binary(OpCode.Mul, i[0], i[1]), i[2])],
            "Brightness and contrast, as multiply then add.");
    }
}