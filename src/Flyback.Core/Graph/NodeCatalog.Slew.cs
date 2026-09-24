using Flyback.Core.Compile;

namespace Flyback.Core.Graph;

public partial class NodeCatalog
{
    public const string SlewTypeId = "audio.slew";

    private const float Remaining = 0.01f;

    private static readonly float SlewTimeConstants = -MathF.Log(Remaining);

    /// <summary>The ADSR's floor: a patched knob can reach below the slider.</summary>
    private const float SlewShortest = 1e-4f;

    /// <summary>
    /// A one-pole lag: glide on a pitch, smoothing on anything else.
    /// </summary>
    /// <remarks>
    /// Exponential, so a glide takes the same time whatever the distance. The knob is
    /// the time to get within <see cref="Remaining"/> of the target.
    /// </remarks>
    private static NodeDef Slew() => new(
        SlewTypeId, "Slew", ModuleCategories.Timing,
        [
            new PortSpec("in", PortKind.Scalar, 0f, -1f, 1f) { Help = "The value to follow." },
            new PortSpec("rise", PortKind.Scalar, -1f, -4f, 1.5f, Display: PortDisplay.Duration)
            {
                Help = "How long it takes to catch a move up, whatever the distance.",
            },
            new PortSpec("fall", PortKind.Scalar, -1f, -4f, 1.5f, Display: PortDisplay.Duration)
            {
                Help = "How long it takes to catch a move down, whatever the distance.",
            },
        ],
        [new PortSpec("out") { Help = "The value, catching up with 'in' as fast as 'rise' and 'fall' let it." }],
        SlewEmit,
        "Follows 'in', taking its time: between a Note Sequencer and a Note it is glide, and "
        + "after a gate it smooths the steps. Audio only: a wire on the picture.")
    {
        Sinks = ModuleSinks.Audio,
    };

    private static Slot[] SlewEmit(Emitter em, EmitContext node)
    {
        var target = node[0];
        var step = em.Interval();
        var live = em.HasMemory();

        var cell = em.AllocateUnitSlot();
        var held = em.UnitRead(cell);

        var rising = em.Binary(OpCode.Step, held, target);
        var time = em.Ternary(OpCode.Mix, Seconds(node[2]), Seconds(node[1]), rising);

        // 1 - e^(-step/tau) rather than step/tau, so a large step closes the gap and no more.
        var tau = em.Mul(time, 1f / SlewTimeConstants);
        var closed = em.Sub(
            em.Constant(1f),
            em.Unary(OpCode.Exp, em.Unary(OpCode.Neg, em.Binary(OpCode.Div, step, tau))));

        // A wire with no memory, and on the first evaluation, so a glide starts at the input.
        var next = em.Ternary(OpCode.Mix, target, em.Ternary(OpCode.Mix, held, target, closed), live);

        // A clock write, because a signal cell is clamped to ±16 and a note number is 60.
        // It stays bounded: the value always lies between what was held and what came in.
        em.ClockWrite(cell, next);

        return [next];

        Slot Seconds(Slot decades) => em.Binary(
            OpCode.Max,
            em.Binary(OpCode.Pow, em.Constant(10f), decades),
            em.Constant(SlewShortest));
    }
}
