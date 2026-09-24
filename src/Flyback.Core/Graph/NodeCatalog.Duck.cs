using Flyback.Core.Compile;

namespace Flyback.Core.Graph;

public partial class NodeCatalog
{
    public const string DuckTypeId = "audio.duck";

    /// <summary>The shortest time Duck's attack or release reaches, whatever is patched in.</summary>
    private const float QuickestFollow = 1e-5f;

    /// <summary>The quietest 'full' reads, so a key is never divided by zero.</summary>
    private const float QuietestFull = 1e-3f;

    /// <summary>
    /// A sidechain: turns a signal down while another one, the key, is loud.
    /// </summary>
    /// <remarks>
    /// The key's level is followed with its own attack and release, so a kick's sound
    /// and a kick's envelope key it alike, and <c>depth</c> is exactly how far a key
    /// at <c>full</c> takes the level down. The cell holds the followed level, which
    /// starts at zero, so an unkeyed Duck is a wire from the first sample.
    /// </remarks>
    private static NodeDef Duck() => new(
        DuckTypeId, "Duck", ModuleCategories.Shaping,
        [
            new PortSpec("left", PatchOnly: true),
            new PortSpec("right", NormalledFrom: 0, PatchOnly: true),
            new PortSpec("key", PatchOnly: true) { Help = "What to make room for: a kick or its envelope." },
            Num("depth", 0.5f, 0f, 1f),
            Num("full", 1f, 0.01f, 2f),
            Seconds("attack", -3f) with { Help = "How fast it follows the key getting louder." },
            Seconds("release", -0.8f) with { Help = "How fast it lets go as the key gets quieter." },
        ],
        [new PortSpec("left"), new PortSpec("right"), new PortSpec("gain") { Help = "The level applied, to duck anything else with." }],
        EmitDuck,
        "Turns 'left' and 'right' down while 'key' is loud. A key as loud as 'full' ducks by "
        + "'depth'.")
    {
        Sinks = ModuleSinks.Audio,
    };

    private static Slot[] EmitDuck(Emitter em, EmitContext node)
    {
        var live = em.HasMemory();
        var zero = em.Constant(0f);
        var one = em.Constant(1f);

        var level = em.Ternary(
            OpCode.Clamp,
            em.Binary(
                OpCode.Div,
                em.Unary(OpCode.Abs, node[2]),
                em.Binary(OpCode.Max, node[4], em.Constant(QuietestFull))),
            zero,
            one);

        var cell = em.AllocateUnitSlot();
        var held = em.UnitRead(cell);

        // Attacking while the key is at least what is held.
        var closing = em.Ternary(
            OpCode.Mix,
            Closing(node[6]),
            Closing(node[5]),
            em.Binary(OpCode.Step, held, level));

        var followed = em.Ternary(OpCode.Mix, held, level, closing);
        em.UnitWrite(cell, followed);

        var gain = em.Sub(one, em.Mul(em.Ternary(OpCode.Clamp, node[3], zero, one), followed));

        return
        [
            em.Ternary(OpCode.Mix, node[0], em.Mul(node[0], gain), live),
            em.Ternary(OpCode.Mix, node[1], em.Mul(node[1], gain), live),
            em.Ternary(OpCode.Mix, one, gain, live),
        ];

        // How much of the way a one-pole lag of that many decades of seconds closes
        // in one evaluation: 1 - e^(-step/tau), so a long step closes the gap and no more.
        Slot Closing(Slot decades)
        {
            var seconds = em.Binary(
                OpCode.Max,
                em.Binary(OpCode.Pow, em.Constant(10f), decades),
                em.Constant(QuickestFollow));

            return em.Sub(
                one,
                em.Unary(OpCode.Exp, em.Unary(OpCode.Neg, em.Binary(OpCode.Div, em.Interval(), seconds))));
        }
    }
}
