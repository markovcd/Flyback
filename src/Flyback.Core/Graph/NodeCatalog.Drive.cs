using Flyback.Core.Compile;

namespace Flyback.Core.Graph;

public partial class NodeCatalog
{
    public const string DriveTypeId = "audio.drive";

    /// <summary>
    /// The least drive the curve is evaluated at. At zero the normalization is
    /// zero over zero, and the whole module would be the one thing worse than
    /// wrong on a video path: black.
    /// </summary>
    private const float Least = 0.05f;

    /// <summary>
    /// Saturation: the gentler half of adding harmonics. Where the Voice plugin's
    /// Fold turns a signal round at full scale, this leans it over and lets it
    /// approach, so what comes out is the same waveform with its peaks rounded off.
    /// </summary>
    /// <remarks>
    /// The curve is <c>x / (1 + |x|)</c>: slope one at the origin, an asymptote at full
    /// scale, and no transcendental function in it. Peak-normalized as it saturates, so
    /// turning drive up makes the signal dirtier and never louder — what it does make is
    /// denser, the quiet parts coming up as the loud ones stop moving.
    /// </remarks>
    private static NodeDef Drive() => new(
        DriveTypeId, "Drive", ModuleCategories.Shaping,
        [
            new PortSpec("in", PortKind.Any, 0f, -1f, 1f) { Help = "The sound or the picture to push." },
            new PortSpec("drive", PortKind.Scalar, 2f, 0f, 16f)
            {
                Help = "How hard it is pushed. Normalized, so more is dirtier and never louder.",
            },
        ],
        [new PortSpec("out", PortKind.Any) { Help = "What went in, its peaks rounded off." }],
        DriveEmit,
        "Soft saturation: rounds the peaks off rather than folding them back, and doubles as "
        + "a compressor. Untyped; on the screen it is contrast that never clips.");

    private static Slot[] DriveEmit(Emitter em, EmitContext inputs)
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
