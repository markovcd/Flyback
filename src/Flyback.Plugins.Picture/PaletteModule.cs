using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Picture;

/// <summary>
/// One number in, a color out, chosen from a palette rather than from the color
/// wheel.
/// </summary>
/// <remarks>
/// Iñigo Quílez's cosine palette,
/// <c>brightness + contrast · cos(2π(cycles · t + offset))</c>, per channel with
/// offsets 'spread' apart. A third is the rainbow, less gives tints of one color,
/// and zero is gray. Unclamped, so 'contrast' past 'brightness' leaves 0 to 1.
/// </remarks>
internal static class PaletteModule
{
    public const string TypeId = "flyback.picture.palette";

    private const float Tau = 6.283185307179586f;

    /// <summary>
    /// The phase step that makes a rainbow, and the default. Three channels a
    /// third of a cycle apart is what a hue sweep is, so the module starts where
    /// the catalog already was and every other setting is a departure from it.
    /// </summary>
    private const float Rainbow = 1f / 3f;

    public static NodeDef Definition { get; } = new(
        TypeId, "Palette", ModuleCategories.Color,
        [
            new PortSpec("t", PortKind.Scalar, 0f, 0f, 1f) { Help = "Where in the palette." },
            new PortSpec("cycles", PortKind.Scalar, 1f, 0f, 8f) { Help = "Repeats the palette across 't'." },
            new PortSpec("spread", PortKind.Scalar, Rainbow, 0f, 1f)
            {
                Help = "The knob to reach for: how far apart the channels are. A third is the "
                    + "rainbow, small is tints of one color, 0 is gray.",
            },
            new PortSpec("brightness", PortKind.Scalar, 0.5f, 0f, 1f) { Help = "The palette's middle." },
            new PortSpec("contrast", PortKind.Scalar, 0.5f, 0f, 1f)
            {
                Help = "How far either side of 'brightness' it reaches. 0 is one flat color.",
            },
        ],
        [new PortSpec("color", PortKind.Color) { Help = "The palette's color at 't'." }],
        Emit,
        "A signal into a color from a palette: where a hue sweep passes through every color, "
        + "this passes through a handful that go together.");

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var along = em.Mul(node[0], node[1]);

        var channels = new Slot[3];

        for (var channel = 0; channel < channels.Length; channel++)
        {
            // The three channels are one wave read at three places. Everything
            // this module is worth is in that sentence: neighbouring phases give
            // neighbouring colors, and a palette is a set of colors that are
            // neighbours.
            var phase = em.Add(along, em.Mul(node[2], channel));

            var wave = em.Unary(OpCode.Cos, em.Mul(phase, Tau));

            channels[channel] = em.Add(node[3], em.Mul(wave, node[4]));
        }

        return [em.Combine(channels[0], channels[1], channels[2])];
    }
}
