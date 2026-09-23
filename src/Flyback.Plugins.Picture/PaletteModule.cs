using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Picture;

/// <summary>
/// One number in, a color out, chosen from a palette rather than from the color
/// wheel.
/// </summary>
/// <remarks>
/// The catalog could already turn a signal into a color one way — into HSV's
/// hue, which walks the whole wheel at full saturation — and that is why so much
/// of what this machine draws looks the same: rainbow is the absence of a palette.
/// <para>
/// This is Iñigo Quílez's cosine palette:
/// <c>brightness + contrast · cos(2π(cycles · t + offset))</c>, three times with
/// the channels' offsets a fixed step apart. Because the three are the same wave
/// at different phases, the colors it passes through are neighbours, and anything
/// neighbouring looks deliberate.
/// </para>
/// <para>
/// 'spread' is the step between those phases and changes the family rather than
/// the position in it: a third is the rainbow, below that the channels move nearly
/// together and the palette runs through tints of one color, and at nothing it is
/// gray. Nothing is clamped — turning 'contrast' past 'brightness' pushes the
/// palette outside 0 to 1, which the screen clips and a Multiply downstream does
/// not.
/// </para>
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
            new PortSpec("t", PortKind.Scalar, 0f, 0f, 1f),
            new PortSpec("cycles", PortKind.Scalar, 1f, 0f, 8f),
            new PortSpec("spread", PortKind.Scalar, Rainbow, 0f, 1f),
            new PortSpec("brightness", PortKind.Scalar, 0.5f, 0f, 1f),
            new PortSpec("contrast", PortKind.Scalar, 0.5f, 0f, 1f),
        ],
        [new PortSpec("color", PortKind.Color)],
        Emit,
        "A signal into a color from a palette: where a hue sweep passes through every color, "
        + "this passes through a handful that go together. 't' is where in the palette, 0 to 1. "
        + "'spread' is the knob to reach for, how far apart the channels are: a third is the "
        + "rainbow, small is tints of one color, 0 is gray. 'cycles' repeats the palette across "
        + "'t'. 'brightness' and 'contrast' are its middle and how far either side; 'contrast' "
        + "0 is one flat color.");

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
