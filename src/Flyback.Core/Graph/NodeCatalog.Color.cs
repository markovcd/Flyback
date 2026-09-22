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
            [Num("hue", 0f, 0f, 1f) with { Lenient = true }, Num("saturation", 1f, 0f, 1f), Num("value", 1f, 0f, 1f)], [Col("color")],
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

        yield return Ink();
        yield return Vignette();
    }

    /// <summary>What an Ink's settings are filed under, and the one it has.</summary>
    public const string InkStateKey = "ink";

    public const string InkModeKey = "mode";

    /// <summary>The mode that is not the default: the color covers what is under it.</summary>
    public const string InkOver = "over";

    private const string InkAdd = "add";

    /// <summary>
    /// A shape given a color and put on the picture: an RGB, a Gain with the shape
    /// on its 'gain', and the Add or the Blend that lays it on what is there.
    /// </summary>
    /// <remarks>
    /// The mode is a setting because it decides which op is emitted. Added, the
    /// color is light: it brightens what it crosses, and nothing under it is lost.
    /// Over, it is paint: a Blend from what is under to the color, by the mask.
    /// With nothing under it the first is the color times the mask, since nought
    /// plus a number is that number.
    /// </remarks>
    private static NodeDef Ink() => new(
        "color.ink", "Ink", ModuleCategories.Color,
        [
            Col("under"),
            Num("mask", 1f, 0f),
            Num("r", 1f, 0f, 1f),
            Num("g", 1f, 0f, 1f),
            Num("b", 1f, 0f, 1f),
        ],
        [Col("color")],
        (em, i) =>
        {
            var ink = em.Combine(i[2], i[3], i[4]);

            return i.Extra<ExtraState>(InkStateKey)?.Chosen(InkModeKey) == InkOver
                ? [em.Ternary(OpCode.Mix, i[0], ink, i[1])]
                : [em.Binary(OpCode.Add, i[0], em.Binary(OpCode.Mul, ink, i[1]))];
        },
        "Draws in one color. Patch a shape into 'mask' — a Fill, a Smoothstep — set the color "
        + "on 'r', 'g' and 'b', and patch the picture so far into 'under', one Ink into the "
        + "next. The mode is set on the node: add lays the color on as light, over covers like "
        + "paint.")
    {
        Extras =
        [
            new SettingsExtra(
                InkStateKey,
                [
                    new ExtraField.Choice(
                        InkModeKey,
                        "mode",
                        [new ChoiceOption(InkAdd, "Add"), new ChoiceOption(InkOver, "Over")],
                        InkAdd),
                ]),
        ],
    };

    /// <summary>
    /// The corners darkened: the radius through a Remap and a Clamp into a Gain,
    /// which is how nearly every finished picture is closed.
    /// </summary>
    /// <remarks>
    /// The Remap's arithmetic with its first output end fixed at one, so inside
    /// 'from' nothing is touched. 'shade' is what the picture was multiplied by,
    /// for a patch that darkens something other than a color with it.
    /// </remarks>
    private static NodeDef Vignette() => new(
        "color.vignette", "Vignette", ModuleCategories.Color,
        [
            Col("color"),
            ..Position(),
            Num("from", 0.5f, 0f),
            Num("to", 2f, 0f),
            Num("dark", 0.35f, 0f, 1f),
        ],
        [Col("color"), Num("shade")],
        (em, i) =>
        {
            var radius = em.Binary(OpCode.Hypot, i[1], i[2]);
            var along = em.Binary(OpCode.Div, em.Binary(OpCode.Sub, radius, i[3]), em.Binary(OpCode.Sub, i[4], i[3]));

            var shade = em.Ternary(
                OpCode.Clamp,
                em.Ternary(OpCode.Mix, em.Constant(1f), i[5], along),
                em.Constant(0f),
                em.Constant(1f));

            return [em.Binary(OpCode.Mul, i[0], shade), shade];
        },
        "Darkens the corners. The picture is untouched out to 'from', measured as Coordinates' "
        + "radius is, and falls to 'dark' of its brightness by 'to'. 'shade' is the darkening "
        + "alone, for a Multiply.")
    {
        Sinks = ModuleSinks.Video,
    };
}