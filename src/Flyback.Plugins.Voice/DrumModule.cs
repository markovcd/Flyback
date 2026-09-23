using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins;

namespace Flyback.Plugins.Voice;

/// <summary>
/// A kick or a tom from one envelope: a sine whose level is the envelope and whose
/// pitch is the envelope bent, through a Drive.
/// </summary>
/// <remarks>
/// The pitch falls faster than the level because it is the level to a power, so the
/// beater is over long before the shell is and there is one envelope to get right
/// rather than two to keep in step. The saturation is the engine's own Drive's
/// curve and its normalization, so a 'drive' here is the same number as one there;
/// at nought it is skipped rather than evaluated at the curve's floor, which is
/// nearly clean and not quite.
/// </remarks>
internal static class DrumModule
{
    public const string TypeId = "flyback.voice.drum";

    private const float Tau = 6.283185307179586f;

    /// <summary>The least drive the curve is evaluated at — see the engine's own Drive.</summary>
    private const float Least = 0.05f;

    /// <summary>Under this 'drive' is off.</summary>
    private const float Off = 1e-3f;

    public static NodeDef Definition { get; } = new(
        TypeId, "Drum", ModuleCategories.Oscillators,
        [
            new PortSpec("in", NormalledTo: NodeCatalog.Clock, Domain: true),
            new PortSpec("level", PortKind.Scalar, 1f, 0f, 1f),
            new PortSpec("pitch", PortKind.Scalar, 50f, 20f, 400f) { Knee = 20f },
            new PortSpec("sweep", PortKind.Scalar, 120f, 0f, 1000f) { Knee = 10f },
            new PortSpec("bend", PortKind.Scalar, 4f, 0.5f, 8f),
            new PortSpec("drive", PortKind.Scalar, 2f, 0f, 16f),
        ],
        [new PortSpec("out")],
        Emit,
        "A kick or a tom. Patch an envelope into 'level' (a Stroke, a Decay, an ADSR) and it "
        + "sets both the loudness and the pitch drop. 'pitch' is where it rests, in hertz: 45 a "
        + "kick, 100 to 250 a tom. 'sweep' is how far above that it starts, 'bend' how fast it "
        + "falls: high clicks, low dives. 'drive' thickens it without making it louder.")
    {
        Skin = new ModuleSkin.Palette(CategoryAccents.Of(ModuleCategories.Oscillators))
        {
            Glyph = "M4,6 A8,3 0 1 1 20,6 A8,3 0 1 1 4,6 M4,6 L4,18 A8,3 0 1 0 20,18 L20,6",
        },
    };

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var one = em.Constant(1f);
        var level = node[1];

        var hz = em.Add(node[2], em.Mul(em.Binary(OpCode.Pow, level, node[4]), node[3]));
        var tone = em.Mul(em.Unary(OpCode.Sin, em.Mul(em.Phase(node[0], hz, em.Constant(0f)), Tau)), level);

        var drive = em.Binary(OpCode.Max, node[5], em.Constant(Least));
        var driven = em.Mul(tone, drive);
        var curve = em.Binary(OpCode.Div, driven, em.Add(em.Unary(OpCode.Abs, driven), 1f));
        var shaped = em.Binary(OpCode.Div, curve, em.Binary(OpCode.Div, drive, em.Add(drive, 1f)));

        // Two products and a sum rather than a Mix, which at either end is its
        // operands exactly only when they are equal.
        var on = em.Binary(OpCode.Step, em.Constant(Off), node[5]);

        return [em.Add(em.Mul(tone, em.Sub(one, on)), em.Mul(shaped, on))];
    }
}
