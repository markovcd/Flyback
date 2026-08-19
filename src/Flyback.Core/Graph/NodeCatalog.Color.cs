using Flyback.Core.Compile;

namespace Flyback.Core.Graph;

public partial class NodeCatalog
{
    private static IEnumerable<NodeDef> Color()
    {
        yield return new NodeDef(
            "colour.rgb", "RGB", "Colour",
            [Num("r", 0f, 0f, 1f), Num("g", 0f, 0f, 1f), Num("b", 0f, 0f, 1f)], [Col("colour")],
            (em, i) => [em.Combine(i[0], i[1], i[2])],
            "Builds a colour from three separate signals.");

        yield return new NodeDef(
            "colour.hsv", "HSV", "Colour",
            [Num("hue", 0f, 0f, 1f), Num("saturation", 1f, 0f, 1f), Num("value", 1f, 0f, 1f)], [Col("colour")],
            (em, i) => [em.Triple(OpCode.HsvToRgb, i[0], i[1], i[2])],
            "Hue, saturation, value. Sweeping hue is the fastest route to rainbows.");

        yield return new NodeDef(
            "colour.split", "Split", "Colour",
            [Col("colour")], [Num("r"), Num("g"), Num("b")],
            (_, i) => [Slot.Scalar(i[0].Base), Slot.Scalar(i[0].Base + 1), Slot.Scalar(i[0].Base + 2)],
            "Pulls a colour apart into its three channels.");

        yield return new NodeDef(
            "colour.mix", "Blend", "Colour",
            [Col("a"), Col("b"), Num("t", 0.5f, 0f, 1f)], [Col("colour")],
            (em, i) => [em.Ternary(OpCode.Mix, i[0], i[1], i[2])],
            "Crossfades between two colours.");

        yield return new NodeDef(
            "colour.gain", "Gain", "Colour",
            [Col("colour"), Any("gain", 1f, 0f), Any("bias", 0f, -1f, 1f)], [Col("colour")],
            (em, i) => [em.Binary(OpCode.Add, em.Binary(OpCode.Mul, i[0], i[1]), i[2])],
            "Brightness and contrast, as multiply then add.");
    }
}