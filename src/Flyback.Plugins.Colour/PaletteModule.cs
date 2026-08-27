using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Colour;

/// <summary>
/// One number in, a colour out, chosen from a palette rather than from the
/// colour wheel.
/// </summary>
/// <remarks>
/// The catalogue could already turn a signal into a colour, and only one way:
/// into HSV's hue, which walks the whole wheel at full saturation. That is why
/// so much of what this machine draws comes out looking the same — rainbow is
/// not a palette, it is the absence of one, and every gradient through it passes
/// through every colour there is on the way to the one that was wanted.
/// <para>
/// What this does instead is Iñigo Quílez's cosine palette:
/// <c>brightness + contrast · cos(2π(cycles · t + offset))</c>, evaluated three
/// times with the three channels' offsets a fixed step apart. It is four
/// multiplies and a cosine per channel, and it is the whole reason a picture can
/// look composed rather than assembled: because the three channels are the same
/// wave at different phases, the colours it passes through are neighbours, and
/// anything neighbouring looks deliberate.
/// </para>
/// <para>
/// 'spread' is the step between those phases and is the one knob that changes the
/// <em>family</em> rather than the position in it. A third is the rainbow, since
/// three channels a third of a cycle apart is exactly what a hue sweep is. Below
/// that the three channels move nearly together and the palette runs through
/// tints of one colour — the sunsets, the teals, the golds. At nothing it is
/// grey, and every value of it is a defensible palette, which is the useful
/// property: a knob that cannot be turned to something ugly.
/// </para>
/// <para>
/// Nothing is clamped. At the default the palette is exactly 0 to 1 in every
/// channel, and turning 'contrast' past 'brightness' pushes it outside — which
/// the screen clips and a Multiply downstream does not, so it is left to say
/// what it means.
/// </para>
/// </remarks>
internal static class PaletteModule
{
    public const string TypeId = "flyback.colour.palette";

    private const float Tau = 6.283185307179586f;

    /// <summary>
    /// The phase step that makes a rainbow, and the default. Three channels a
    /// third of a cycle apart is what a hue sweep is, so the module starts where
    /// the catalogue already was and every other setting is a departure from it.
    /// </summary>
    private const float Rainbow = 1f / 3f;

    public static NodeDef Definition { get; } = new(
        TypeId, "Palette", "Color",
        [
            new PortSpec("t", PortKind.Scalar, 0f, 0f, 1f),
            new PortSpec("cycles", PortKind.Scalar, 1f, 0f, 8f),
            new PortSpec("spread", PortKind.Scalar, Rainbow, 0f, 1f),
            new PortSpec("brightness", PortKind.Scalar, 0.5f, 0f, 1f),
            new PortSpec("contrast", PortKind.Scalar, 0.5f, 0f, 1f),
        ],
        [new PortSpec("color", PortKind.Color)],
        Emit,
        "Turns a signal into a colour from a palette, which is what an HSV hue is not: a hue "
        + "sweep walks the whole wheel and passes through every colour there is, and this "
        + "passes through a handful that go together. 't' is where in the palette to look, 0 to "
        + "1. 'spread' is the knob to reach for — it is how far apart the three channels are, "
        + "so a third is the rainbow, small values are tints of one colour, and nothing at all "
        + "is grey. 'cycles' repeats the palette across 't', which bands a gradient. "
        + "'brightness' and 'contrast' are the middle of the palette and how far either side of "
        + "it, so contrast at nothing is one flat colour. Sweep 'spread' or 'cycles' from an "
        + "oscillator and the picture changes its mind about what it is coloured with rather "
        + "than merely rotating.");

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var along = em.Mul(node[0], node[1]);

        var channels = new Slot[3];

        for (var channel = 0; channel < channels.Length; channel++)
        {
            // The three channels are one wave read at three places. Everything
            // this module is worth is in that sentence: neighbouring phases give
            // neighbouring colours, and a palette is a set of colours that are
            // neighbours.
            var phase = em.Add(along, em.Mul(node[2], channel));

            var wave = em.Unary(OpCode.Cos, em.Mul(phase, Tau));

            channels[channel] = em.Add(node[3], em.Mul(wave, node[4]));
        }

        return [em.Combine(channels[0], channels[1], channels[2])];
    }
}
