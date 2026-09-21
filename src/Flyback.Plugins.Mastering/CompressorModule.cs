using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins;

namespace Flyback.Plugins.Mastering;

/// <summary>
/// A compressor: turns a signal down by an amount that depends on how loud it
/// is, so the loud parts come nearer the quiet ones.
/// </summary>
/// <remarks>
/// Feed-forward and stereo-linked. Both sides are turned down by one gain, worked
/// out from the louder of the two, so a stereo picture does not lean towards
/// whichever side is quieter. The gain computer is the soft-knee curve from
/// Giannoulis, Massberg and Reiss. It is worked out on each sample's own level
/// and then smoothed, so attack and release shape the gain rather than the level
/// it was read from.
/// <para>
/// The smoothed gain is held in a cell as the reduction, one minus the gain,
/// because a cell starts at zero. Held as the gain, a compressor would start
/// silent and fade in over its release.
/// </para>
/// </remarks>
internal static class CompressorModule
{
    public const string TypeId = "flyback.mastering.compressor";

    /// <summary>
    /// The quietest level the gain computer reads, in linear terms (-100 dB).
    /// The log of silence is minus infinity, and nothing is ever turned down
    /// that quietly anyway.
    /// </summary>
    private const float Floor = 1e-5f;

    /// <summary>
    /// The narrowest knee the curve is evaluated with. At zero the soft half
    /// divides by zero. At this width it is a hard knee to any ear.
    /// </summary>
    private const float Hardest = 1e-3f;

    private const int Left = 0;
    private const int Right = 1;
    private const int Key = 2;
    private const int Threshold = 3;
    private const int Ratio = 4;
    private const int Attack = 5;
    private const int Release = 6;
    private const int Knee = 7;
    private const int Makeup = 8;

    public static NodeDef Definition { get; } = new(
        TypeId, "Compressor", ModuleCategories.Shaping,
        [
            new PortSpec("left", PatchOnly: true),
            new PortSpec("right", NormalledFrom: Left, PatchOnly: true),
            new PortSpec("key", NormalledFrom: Left, PatchOnly: true),
            new PortSpec("threshold", PortKind.Scalar, -18f, -60f, 0f),
            new PortSpec("ratio", PortKind.Scalar, 4f, 1f, 20f),
            new PortSpec("attack", PortKind.Scalar, -2f, -4f, 0f, Display: PortDisplay.Duration),
            new PortSpec("release", PortKind.Scalar, -1f, -3f, 1f, Display: PortDisplay.Duration),
            new PortSpec("knee", PortKind.Scalar, 6f, 0f, 24f),
            new PortSpec("makeup", PortKind.Scalar, 0f, 0f, 24f),
        ],
        [new PortSpec("left"), new PortSpec("right"), new PortSpec("gain")],
        Emit,
        "Turns loud passages down. Over 'threshold' (dB), 'ratio' dB in comes out as one; 'knee' "
        + "(dB) rounds the corner, 'makeup' (dB) lifts it. Patch 'key' to duck. 'gain' is the "
        + "gain applied.")
    {
        Sinks = ModuleSinks.Audio,
        Skin = new ModuleSkin.Palette(CategoryAccents.Of(ModuleCategories.Shaping))
        {
            Glyph = "M3,20 L11,12 L21,10 M9.7,12 A1.3,1.3 0 1 1 12.3,12 A1.3,1.3 0 1 1 9.7,12",
        },
    };

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var live = em.HasMemory();
        var one = em.Constant(1f);

        // Unpatched, 'key' is handed the very slot 'left' is. That is how the
        // module tells listening to itself, which means both sides, from
        // listening to a key.
        var level = node[Key] == node[Left]
            ? em.Binary(OpCode.Max, em.Unary(OpCode.Abs, node[Left]), em.Unary(OpCode.Abs, node[Right]))
            : em.Unary(OpCode.Abs, node[Key]);

        var gain = Gain(em, level, node[Threshold], node[Ratio], node[Attack], node[Release], node[Knee]);
        var applied = em.Mul(gain, Dsp.Linear(em, node[Makeup]));

        return
        [
            Dsp.Pick(em, node[Left], em.Mul(node[Left], applied), live),
            Dsp.Pick(em, node[Right], em.Mul(node[Right], applied), live),
            Dsp.Pick(em, one, gain, live),
        ];
    }

    /// <summary>
    /// The smoothed gain for a detector reading <paramref name="level"/>, for a
    /// module with a compressor inside it — see <see cref="MaximizerModule"/>.
    /// </summary>
    /// <param name="threshold">In dB.</param>
    /// <param name="attack">A power of ten of seconds, as the socket has it.</param>
    /// <param name="release">A power of ten of seconds.</param>
    /// <param name="knee">In dB.</param>
    public static Slot Gain(
        Emitter em, Slot level, Slot threshold, Slot ratio, Slot attack, Slot release, Slot knee)
    {
        var one = em.Constant(1f);

        var over = em.Sub(Dsp.Decibels(em, level, Floor), threshold);

        // The gain computer's slope above the knee: 1/ratio - 1, so a ratio of
        // one is no slope and the module a wire.
        var slope = em.Sub(em.Binary(OpCode.Div, one, em.Binary(OpCode.Max, ratio, one)), one);

        var width = em.Binary(OpCode.Max, knee, em.Constant(Hardest));
        var half = em.Mul(width, 0.5f);

        var hard = em.Mul(slope, em.Binary(OpCode.Max, over, em.Constant(0f)));
        var into = em.Add(over, half);
        var soft = em.Binary(OpCode.Div, em.Mul(slope, em.Mul(into, into)), em.Mul(width, 2f));
        var inKnee = Dsp.AtLeast(em, half, em.Unary(OpCode.Abs, over));

        var wanted = Dsp.Linear(em, Dsp.Pick(em, hard, soft, inKnee));

        var cell = em.AllocateUnitSlot();
        var held = em.Sub(one, em.UnitRead(cell));

        // Attacking when the gain wanted is below the gain held.
        var attacking = Dsp.AtLeast(em, held, wanted);
        var closing = Dsp.Pick(
            em,
            Dsp.Closing(em, Dsp.Seconds(em, release)),
            Dsp.Closing(em, Dsp.Seconds(em, attack)),
            attacking);

        var gain = em.Ternary(OpCode.Mix, held, wanted, closing);

        em.UnitWrite(cell, em.Sub(one, gain));

        return gain;
    }
}
