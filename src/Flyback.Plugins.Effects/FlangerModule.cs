using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins;

namespace Flyback.Plugins.Effects;

/// <summary>
/// The same delay as a chorus, an order of magnitude shorter and fed back on itself.
/// At a few milliseconds a copy no longer thickens a sound — it cancels parts of it,
/// and sweeping the delay drags that comb of notches up and down the spectrum.
/// </summary>
/// <remarks>
/// Feedback sharpens the notches from dips into slots, and is signed here rather than
/// positive: negative feedback inverts the comb, and the two sound different enough
/// that offering one would be leaving half the module out.
/// </remarks>
internal static class FlangerModule
{
    public const string TypeId = "flyback.effects.flanger";

    /// <summary>
    /// Short, and deliberately never reaching zero: the shortest delay a line can
    /// express is one evaluation (ADR-0027), so through-zero flanging is not
    /// something this can do however the numbers are arranged.
    /// </summary>
    private const float Center = 0.0027f;

    private const float Swing = 0.0024f;

    private const float Longest = Center + Swing;

    public static NodeDef Definition { get; } = new(
        TypeId, "Flanger", ModuleCategories.TimeEffects,
        [
            Sweep.Input,
            Sweep.Rate(0.3f, 5f),
            Sweep.Depth(0.8f),
            new PortSpec("feedback", PortKind.Scalar, 0.5f, -0.95f, 0.95f),
            Sweep.Mix(0.5f),
        ],
        [new PortSpec("out"), Sweep.Motion],
        Emit,
        "A very short chorus whose copy cancels the original, sweeping a comb of notches "
        + "through the sound. 'feedback' sharpens them, and negative moves them to where the "
        + "peaks were. 'lfo' is the sweep and works on the picture; otherwise it is audio only, "
        + "a wire.")
    {
        Skin = new ModuleSkin.Palette(CategoryAccents.Of(ModuleCategories.TimeEffects))
        {
            Glyph = "M2,12 L4,4 L6,12 L8,4 L10,12 L12,4 L14,12 L16,4 L18,12 L20,4 L22,12",
        },
    };

    private static Slot[] Emit(Emitter em, EmitContext inputs)
    {
        var dry = inputs[0];
        var lfo = Sweep.Of(em, inputs[1]);

        var swing = em.Mul(em.Ternary(OpCode.Clamp, inputs[2], em.Constant(0f), em.Constant(1f)), Swing);
        var time = em.Add(em.Constant(Center), em.Mul(lfo, swing));

        var wet = em.DelayLine(OpCode.Delay, dry, inputs[3], time, Longest);

        return [em.Ternary(OpCode.Mix, dry, wet, inputs[4]), lfo];
    }
}
