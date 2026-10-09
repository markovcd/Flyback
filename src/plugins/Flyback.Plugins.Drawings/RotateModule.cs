using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Drawings;

/// <summary>
/// A point in 3D turned about all three axes: yaw about the vertical, then pitch
/// toward the viewer, then roll in the plane of the screen.
/// </summary>
internal static class RotateModule
{
    public const string TypeId = "flyback.drawings.rotate3d";

    public const int YawPort = 3;
    public const int PitchPort = 4;
    public const int RollPort = 5;

    private const float Tau = 6.283185307179586f;

    public static NodeDef Definition { get; } = new(
        TypeId, "Rotate 3D", DrawingsPlugin.Category,
        [
            ..Space3d.Point(),
            Angle("yaw", "About the vertical, in radians: turns it like a turntable."),
            Angle("pitch", "About the horizontal, in radians: tips the top toward the viewer."),
            Angle("roll", "In the plane of the screen, in radians: turns it like a wheel."),
        ],
        [.. Space3d.Point("Across, turned.", "Up, turned.", "Toward the viewer, turned.")],
        Emit,
        "Turns a point in 3D, such as a Path's x, y and z, by yaw, pitch and roll in that order. "
        + "A clock or an oscillator on an angle makes it spin.")
    {
        Words = "turn in 3D",
        Skin = Art.Skin("rotate3d"),
    };

    private static PortSpec Angle(string name, string help) =>
        new(name, PortKind.Scalar, 0f, -Tau, Tau) { Help = help };

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var (x, y, z) = (node[Space3d.XPort], node[Space3d.YPort], node[Space3d.ZPort]);

        // Yaw mixes across with depth, pitch up with depth, roll across with up.
        (x, z) = Turned(em, x, z, node[YawPort]);
        (y, z) = Turned(em, y, z, node[PitchPort], toward: true);
        (x, y) = Turned(em, x, y, node[RollPort], toward: true);

        return [x, y, z];
    }

    /// <summary>A pair turned by an angle: <paramref name="toward"/> turns the first into the second.</summary>
    private static (Slot, Slot) Turned(Emitter em, Slot first, Slot second, Slot angle, bool toward = false)
    {
        var cos = em.Unary(OpCode.Cos, angle);
        var sin = em.Unary(OpCode.Sin, angle);

        return toward
            ? (em.Sub(em.Mul(first, cos), em.Mul(second, sin)), em.Add(em.Mul(first, sin), em.Mul(second, cos)))
            : (em.Add(em.Mul(first, cos), em.Mul(second, sin)), em.Sub(em.Mul(second, cos), em.Mul(first, sin)));
    }
}
