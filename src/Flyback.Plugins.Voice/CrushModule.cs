using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins;

namespace Flyback.Plugins.Voice;

/// <summary>
/// A bitcrusher: fewer levels and fewer samples, the two ways an old sampler or a
/// console's sound chip got a signal wrong.
/// </summary>
/// <remarks>
/// The levels are pure and the samples are not: holding takes a cell, which the
/// screen does not have, so on the picture 'rate' is a wire and 'bits' bands a
/// gradient. Nothing is filtered on either side of the hold, because the aliasing
/// is what the module is for.
/// </remarks>
internal static class CrushModule
{
    public const string TypeId = "flyback.voice.crush";

    public static NodeDef Definition { get; } = new(
        TypeId, "Crush", ModuleCategories.Shaping,
        [
            new PortSpec("in", PortKind.Scalar, 0f, -1f, 1f),
            new PortSpec("bits", PortKind.Scalar, 8f, 1f, 16f, Display: PortDisplay.Integer)
            {
                Help = "How many levels are left across full scale. On the picture it bands a gradient.",
            },
            new PortSpec("rate", PortKind.Scalar, 8_000f, 50f, 48_000f)
            {
                Knee = 50f,
                Help = "Samples taken and held a second. Below the pitch it aliases into new notes; "
                    + "on the picture it holds nothing.",
            },
        ],
        [new PortSpec("out")],
        Emit,
        "Crunches a signal down to fewer levels and fewer samples.")
    {
        Skin = new ModuleSkin.Palette(CategoryAccents.Of(ModuleCategories.Shaping))
        {
            Glyph = "M3,18 L3,14 L7,14 L7,8 L11,8 L11,4 L15,4 L15,10 L19,10 L19,16 L21,16",
        },
    };

    private static Slot[] Emit(Emitter em, EmitContext inputs)
    {
        var one = em.Constant(1f);
        var live = em.HasMemory();

        var heldCell = em.AllocateUnitSlot();
        var phaseCell = em.AllocateUnitSlot();

        // A phase that wraps at each sample taken, so the rate is in hertz whatever
        // the evaluation rate is.
        var phase = em.Add(em.UnitRead(phaseCell), em.Mul(em.Interval(), inputs[2]));
        var due = em.Binary(OpCode.Step, one, phase);

        // Take one where there is nothing held yet, which on the screen is always.
        var take = em.Binary(OpCode.Max, due, em.Sub(one, live));
        var held = em.Ternary(OpCode.Mix, em.UnitRead(heldCell), inputs[0], take);

        em.UnitWrite(phaseCell, em.Unary(OpCode.Fract, phase));

        // A clock write, so a held note number is not clamped to the rails. Bounded
        // all the same: it is always an input already seen.
        em.ClockWrite(heldCell, held);

        // Half the levels either side of zero, so one bit is -1, 0 and 1.
        var levels = em.Binary(
            OpCode.Pow, em.Constant(2f), em.Add(em.Binary(OpCode.Max, inputs[1], one), -1f));
        var stepped = em.Unary(OpCode.Floor, em.Add(em.Mul(held, levels), 0.5f));

        return [em.Binary(OpCode.Div, stepped, levels)];
    }
}
