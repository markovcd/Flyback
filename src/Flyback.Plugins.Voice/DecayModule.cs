using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Voice;

/// <summary>
/// A struck envelope: a trigger starts it, it rises, then falls away on its own.
/// </summary>
/// <remarks>
/// Unlike the ADSR it needs no held gate, so a one-evaluation pulse is enough. The
/// ramp is linear and the curve is a power applied to it, so the knobs stay honest:
/// the fall still reaches silence exactly when 'decay' says.
/// </remarks>
internal static class DecayModule
{
    public const string TypeId = "flyback.voice.decay";

    private const float TriggerOpen = 0.5f;

    /// <summary>The ADSR's floor: a patched knob can reach below the slider.</summary>
    private const float Shortest = 1e-4f;

    /// <summary>The power at a curve of 1.</summary>
    private const float Steepest = 4f;

    public static NodeDef Definition { get; } = new(
        TypeId, "Decay", ModuleCategories.Timing,
        [
            new PortSpec("trigger", PortKind.Scalar, 0f, 0f, 1f),
            new PortSpec("attack", PortKind.Scalar, -3f, -4f, 1.5f, Display: PortDisplay.Duration),
            new PortSpec("decay", PortKind.Scalar, -0.7f, -4f, 1.5f, Display: PortDisplay.Duration),
            new PortSpec("curve", PortKind.Scalar, 0.6f, 0f, 1f),
        ],
        [new PortSpec("out", PortKind.Scalar, 0f, 0f, 1f)],
        Emit,
        "A percussive envelope. Each rise of 'trigger' — a Sequencer's gate, a MIDI trigger, "
        + "a Euclid — sends it up over 'attack' and back to silence over 'decay', without "
        + "waiting for the trigger to fall. A new hit mid-fall rises from where it is, so fast "
        + "hits do not click. 'curve' at 0 falls in a straight line; turned up it drops fast "
        + "and leaves a long tail, the way a drum does. Audio only: on the picture it passes "
        + "the trigger through.")
    {
        Sinks = ModuleSinks.Audio,
    };

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var zero = em.Constant(0f);
        var one = em.Constant(1f);

        var step = em.Interval();
        var live = em.HasMemory();

        var levelCell = em.AllocateUnitSlot();
        var risingCell = em.AllocateUnitSlot();
        var triggerCell = em.AllocateUnitSlot();

        var level = em.UnitRead(levelCell);

        var open = em.Binary(OpCode.Step, em.Constant(TriggerOpen), node[0]);
        var struck = em.Mul(open, em.Sub(one, em.UnitRead(triggerCell)));
        var rising = em.Binary(OpCode.Max, struck, em.UnitRead(risingCell));

        var up = em.Binary(OpCode.Min, em.Add(level, em.Binary(OpCode.Div, step, Seconds(node[1]))), one);
        var down = em.Binary(OpCode.Max, em.Sub(level, em.Binary(OpCode.Div, step, Seconds(node[2]))), zero);
        var next = em.Ternary(OpCode.Mix, down, up, rising);

        // The attack ends the evaluation the peak is reached.
        em.UnitWrite(levelCell, next);
        em.UnitWrite(risingCell, em.Mul(rising, em.Sub(one, em.Binary(OpCode.Step, one, up))));
        em.UnitWrite(triggerCell, open);

        var power = em.Add(em.Mul(em.Ternary(OpCode.Clamp, node[3], zero, one), Steepest - 1f), 1f);
        var shaped = em.Binary(OpCode.Pow, next, power);

        // With no memory there is nothing to fall from, so show the trigger, as the ADSR shows its gate.
        return [em.Ternary(OpCode.Mix, em.Ternary(OpCode.Clamp, node[0], zero, one), shaped, live)];

        Slot Seconds(Slot decades) => em.Binary(
            OpCode.Max,
            em.Binary(OpCode.Pow, em.Constant(10f), decades),
            em.Constant(Shortest));
    }
}
