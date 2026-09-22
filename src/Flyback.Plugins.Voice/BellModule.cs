using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins;

namespace Flyback.Plugins.Voice;

/// <summary>
/// Struck metal out of two sines: one at the pitch, and one at a ratio above it
/// leaning on the first one's phase.
/// </summary>
/// <remarks>
/// How hard the second leans is the envelope, so the note is bright when it is hit
/// and pure by the time it has rung, which is the whole character of a bell for two
/// oscillators. The arithmetic is two Sines and the two Multiplies that feed the
/// upper one, in the order they emit it: a ratio that is not a whole number is
/// what makes it metal rather than an organ.
/// </remarks>
internal static class BellModule
{
    public const string TypeId = "flyback.voice.bell";

    private const float Tau = 6.283185307179586f;

    public static NodeDef Definition { get; } = new(
        TypeId, "Bell", ModuleCategories.Oscillators,
        [
            new PortSpec("in", NormalledTo: NodeCatalog.Clock, Domain: true),
            new PortSpec("freq", PortKind.Scalar, 440f, 20f, 4000f) { Knee = 20f },
            new PortSpec("level", PortKind.Scalar, 1f, 0f, 1f),
            new PortSpec("ratio", PortKind.Scalar, 2.76f, 0.25f, 16f),
            new PortSpec("index", PortKind.Scalar, 0.3f, 0f, 2f),
        ],
        [new PortSpec("out")],
        Emit,
        "A bell, a gong, a chime. Patch an envelope into 'level' — a Stroke, a Decay — and a "
        + "frequency into 'freq'. 'ratio' is where the overtone sits above the pitch: a whole "
        + "number is a clean organ tone, 2.76 is a bronze bar, 1.41 a small bright chime, 3.5 "
        + "glass. 'index' is how much of it there is when the note is struck; it fades with the "
        + "level, so every note starts bright and rings pure.")
    {
        Skin = new ModuleSkin.Palette(CategoryAccents.Of(ModuleCategories.Oscillators))
        {
            Glyph = "M12,3 C8,3 8,8 6,10 C4,12 3,14 3,16 L21,16 C21,14 20,12 18,10 C16,8 16,3 12,3 Z "
                + "M9,18.5 A3,3 0 0 0 15,18.5",
        },
    };

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var nought = em.Constant(0f);
        var level = node[2];

        var overtone = Sine(em.Mul(node[1], node[3]), nought, em.Mul(level, node[4]));

        return [Sine(node[1], overtone, level)];

        // An oscillator's arithmetic, less the 'bias' that at rest adds nought.
        Slot Sine(Slot hz, Slot phase, Slot amp) =>
            em.Mul(em.Unary(OpCode.Sin, em.Mul(em.Phase(node[0], hz, phase), Tau)), amp);
    }
}
