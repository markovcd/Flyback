using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Effects;

/// <summary>
/// The slow sine all three modules are built around, and the sockets they
/// describe it with. What separates a chorus from a flanger from a phaser is
/// almost entirely what this is wired to and how far it swings.
/// </summary>
internal static class Sweep
{
    /// <summary>
    /// The oscillator is inside the module rather than on a socket, which is the one
    /// place this plugin departs from how the rest of the synth is wired: the effect
    /// is its own movement, and a chorus whose sweep has to be built by hand is three
    /// modules pretending to be one.
    /// </summary>
    /// <remarks>
    /// The sine is handed back on an output as well, so a patch can see the movement
    /// it is hearing. That output is the one part of these modules that works on the
    /// picture, a phase accumulator falling back to the multiply it replaced where
    /// there is no state (ADR-0030).
    /// </remarks>
    public static Slot Of(Emitter em, Slot rate) =>
        em.Unary(OpCode.Sin, em.Mul(em.Phase(em.Load(OpCode.LoadT), rate, em.Constant(0f)), MathF.Tau));

    public static PortSpec Input => new("in", PortKind.Scalar, 0f, -1f, 1f);

    /// <summary>Cycles per second, and slow: past a few hertz all three stop being effects and start being tremolo.</summary>
    public static PortSpec Rate(float value, float most) =>
        new("rate", PortKind.Scalar, value, 0.02f, most);

    /// <summary>How far the sweep swings, as a fraction of what the module allows.</summary>
    public static PortSpec Depth(float value) => new("depth", PortKind.Scalar, value, 0f, 1f);

    /// <summary>
    /// Dry against wet. At 0 every module here is exactly a wire, which is the
    /// same promise the Delay makes and worth keeping for the same reason.
    /// </summary>
    public static PortSpec Mix(float value) => new("mix", PortKind.Scalar, value, 0f, 1f);

    public static PortSpec Motion => new("lfo");
}
