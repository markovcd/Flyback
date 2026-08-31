using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Voice;

/// <summary>
/// Saturation: the gentler half of adding harmonics. Where <see cref="FoldModule"/>
/// turns a signal round at full scale, this leans it over and lets it approach —
/// so what comes out is the same waveform with its peaks rounded off rather than
/// a different waveform altogether.
/// </summary>
/// <remarks>
/// The curve is <c>x / (1 + |x|)</c>: slope one at the origin, an asymptote at
/// full scale, and no transcendental function in it. A <c>tanh</c> would be the
/// textbook answer and sounds all but identical after the drive is normalised.
/// <para>
/// It is peak-normalised as it saturates, the way the Supersaw is as it
/// spreads: the curve is divided by what it does to a full-scale input, so
/// turning drive up makes the signal dirtier and never louder. What it does make
/// is *denser* — the quiet parts come up as the loud ones stop moving, which is
/// what saturation is and why a compressor and a distortion are the same
/// arithmetic at different settings.
/// </para>
/// </remarks>
internal static class DriveModule
{
    public const string TypeId = "flyback.voice.drive";

    /// <summary>
    /// The least drive the curve is evaluated at. At zero the normalisation is
    /// zero over zero, and the whole module would be the one thing worse than
    /// wrong on a video path: black.
    /// </summary>
    private const float Least = 0.05f;

    public static NodeDef Definition { get; } = new(
        TypeId, "Drive", ModuleCategories.Shaping,
        [
            new PortSpec("in", PortKind.Any, 0f, -1f, 1f),
            new PortSpec("drive", PortKind.Scalar, 2f, 0f, 16f),
        ],
        [new PortSpec("out", PortKind.Any)],
        Emit,
        "Soft saturation. Rounds the peaks off a signal instead of folding them back, which "
        + "is the difference between a tone that thickens and one that changes shape. "
        + "Normalised as it goes, so more drive is dirtier and never louder — and because "
        + "the quiet parts come up while the loud ones stop moving, it doubles as a "
        + "compressor. Untyped, and on the screen it reads as contrast that never clips.");

    private static Slot[] Emit(Emitter em, EmitContext inputs)
    {
        var drive = em.Binary(OpCode.Max, inputs[1], em.Constant(Least));
        var driven = em.Mul(inputs[0], drive);

        var curve = em.Binary(
            OpCode.Div, driven, em.Add(em.Unary(OpCode.Abs, driven), 1f));

        // What the curve does to a full-scale input, divided back out. Drive is
        // already positive, so this is the same expression as above with the
        // absolute value already known.
        var ceiling = em.Binary(OpCode.Div, drive, em.Add(drive, 1f));

        return [em.Binary(OpCode.Div, curve, ceiling)];
    }
}
