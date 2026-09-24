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
            [
                Num("r", 1f, 0f, 1f) with { Help = Red },
                Num("g", 1f, 0f, 1f) with { Help = Green },
                Num("b", 1f, 0f, 1f) with { Help = Blue },
            ],
            [Col("color") with { Help = "The three channels as one color." }],
            (em, i) => [em.Combine(i[0], i[1], i[2])],
            "Builds a color from three separate signals. It starts white — every channel full — "
            + "so turning one down tints it and patching a signal into one drives that channel "
            + "against the other two.");

        yield return new NodeDef(
            "color.hsv", "HSV", ModuleCategories.Color,
            [
                Num("hue", 0f, 0f, 1f) with
                {
                    Lenient = true,
                    Help = "Round the color wheel: 1 is once round, so it wraps. Sweeping it is the fastest route to rainbows.",
                },
                Num("saturation", 1f, 0f, 1f) with { Help = "How much color: 0 is gray." },
                Num("value", 1f, 0f, 1f) with { Help = "Brightness: 0 is black." },
            ],
            [Col("color") with { Help = "The color, as red, green and blue." }],
            (em, i) => [em.Triple(OpCode.HsvToRgb, i[0], i[1], i[2])],
            "Builds a color from hue, saturation and value.");

        yield return new NodeDef(
            "color.split", "Split", ModuleCategories.Color,
            [Col("color") with { Help = "The color to pull apart." }],
            [Num("r") with { Help = Red }, Num("g") with { Help = Green }, Num("b") with { Help = Blue }],
            (_, i) => [Slot.Scalar(i[0].Base), Slot.Scalar(i[0].Base + 1), Slot.Scalar(i[0].Base + 2)],
            "Pulls a color apart into its three channels.");

        yield return new NodeDef(
            "color.mix", "Blend", ModuleCategories.Color,
            [
                Col("a") with { Help = "The color at 't' 0." },
                Col("b") with { Help = "The color at 't' 1." },
                Num("t", 0.5f, 0f, 1f) with { Help = SocketHelp.Blend },
            ],
            [Col("color") with { Help = Blended }],
            (em, i) => [em.Ternary(OpCode.Mix, i[0], i[1], i[2])],
            "Crossfades between two colors.");

        yield return new NodeDef(
            "color.gain", "Gain", ModuleCategories.Color,
            [
                Col("color") with { Help = Changed },
                Any("gain", 1f, 0f) with { Help = "Multiplies every channel. A color here tints." },
                Any("bias", 0f, -1f, 1f) with { Help = "Added after 'gain', lifting or sinking every channel." },
            ],
            [Col("color") with { Help = Worked }],
            (em, i) => [em.Binary(OpCode.Add, em.Binary(OpCode.Mul, i[0], i[1]), i[2])],
            "Brightness and contrast, as multiply then add.");

        yield return Ink();
        yield return Vignette();
    }

    /// <summary>What an Ink's settings are filed under, and the one it has.</summary>
    private const string Red = "Red, 0 to 1.", Green = "Green, 0 to 1.", Blue = "Blue, 0 to 1.";

    /// <summary>The help on a color a module works on, and on what it makes of it.</summary>
    private const string Changed = "The picture it works on.", Worked = "The picture, worked on.";

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
            Col("under") with { Help = "The picture so far." },
            Num("mask", 1f, 0f) with { Help = "The shape to draw, 1 inside and 0 out: a Fill, a Smoothstep." },
            Num("r", 1f, 0f, 1f) with { Help = "The ink's red, 0 to 1." },
            Num("g", 1f, 0f, 1f) with { Help = "The ink's green, 0 to 1." },
            Num("b", 1f, 0f, 1f) with { Help = "The ink's blue, 0 to 1." },
        ],
        [Col("color") with { Help = "The picture with the ink on it." }],
        (em, i) =>
        {
            var ink = em.Combine(i[2], i[3], i[4]);

            return i.Extra<ExtraState>(InkStateKey)?.Chosen(InkModeKey) == InkOver
                ? [em.Ternary(OpCode.Mix, i[0], ink, i[1])]
                : [em.Binary(OpCode.Add, i[0], em.Binary(OpCode.Mul, ink, i[1]))];
        },
        "Draws in one color, set on 'r', 'g' and 'b'. Chain them by patching one Ink's 'color' "
        + "into the next one's 'under'. The mode is set on the node: add lays the color on as "
        + "light, over covers like paint.")
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
            Col("color") with { Help = Changed },
            ..Position(),
            Num("from", 0.5f, 0f) with { Help = "The radius out to which nothing is touched, as Coordinates measures it." },
            Num("to", 2f, 0f) with { Help = "The radius where it reaches 'dark'." },
            Num("dark", 0.35f, 0f, 1f) with { Help = "The share of its brightness the picture keeps at 'to'." },
        ],
        [Col("color") with { Help = "The picture, darkened toward its edges." }, Num("shade") with { Help = "The darkening alone, for a Multiply." }],
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
        "Darkens the corners, from untouched at 'from' to 'dark' at 'to'.")
    {
        Sinks = ModuleSinks.Video,
    };
}