using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Voice;

/// <summary>
/// A part's entry in an arrangement: a signal let through once a level has risen
/// far enough.
/// </summary>
/// <remarks>
/// An arrangement here is one number per section saying how much track there is,
/// and each part decides how much of it it needs before it comes in. That decision
/// is a Smoothstep into a Multiply, once per part; this is the pair, with the
/// Smoothstep handed out as well because the picture usually wants the same entry
/// the sound got.
/// </remarks>
internal static class FadeModule
{
    public const string TypeId = "flyback.voice.fade";

    public static NodeDef Definition { get; } = new(
        TypeId, "Fade", ModuleCategories.Timing,
        [
            new PortSpec("in", PortKind.Any, 1f, -1f, 1f),
            new PortSpec("level", PortKind.Scalar, 0f, 0f, 1f),
            new PortSpec("from", PortKind.Scalar, 0f, 0f, 1f),
            new PortSpec("to", PortKind.Scalar, 1f, 0f, 1f),
        ],
        [new PortSpec("out", PortKind.Any), new PortSpec("gate", PortKind.Scalar, 0f, 0f, 1f)],
        Emit,
        "Brings a part in. 'in' is silent while 'level' is under 'from', at full once it is "
        + "over 'to', and fades smoothly between. Drive 'level' from one Sequencer that says "
        + "how far the track has got and give every part its own 'from' and 'to': that is a "
        + "whole arrangement on one lane. Set 'from' above 'to' and the part leaves as the "
        + "level rises instead. 'gate' is the fade alone, 0 to 1, for whatever should arrive "
        + "with the part — a color, a filter. Untyped, so it fades a picture as readily as a "
        + "voice.");

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var gate = em.Ternary(OpCode.Smoothstep, node[2], node[3], node[1]);

        return [em.Mul(node[0], gate), gate];
    }
}
