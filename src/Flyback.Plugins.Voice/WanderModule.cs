using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins;

namespace Flyback.Plugins.Voice;

/// <summary>
/// One slow random voltage: the engine's value noise walked along a single line,
/// and rescaled to the range it is wanted over.
/// </summary>
/// <remarks>
/// A Clouds module does this once its x and y are pinned by a Value — left to their
/// normal they read the pixel, and the screen gets a field where the speakers get a
/// number. Here the pin is 'seed', on both axes, so the two sinks cannot disagree.
/// The engine's own Noise's drift is the same lookup, between -1 and 1 at a
/// rate counted in steps; this is the one with the range on it.
/// </remarks>
internal static class WanderModule
{
    public const string TypeId = "flyback.voice.wander";

    public static NodeDef Definition { get; } = new(
        TypeId, "Wander", ModuleCategories.Oscillators,
        [
            new PortSpec("in", NormalledTo: NodeCatalog.Clock, Domain: true),
            new PortSpec("rate", PortKind.Scalar, 0.1f, 0f, 8f),
            new PortSpec("seed", PortKind.Scalar, 0f, 0f, 16f),
            new PortSpec("low"),
            new PortSpec("high", PortKind.Scalar, 1f),
        ],
        [new PortSpec("out")],
        Emit,
        "A smooth random value that never repeats, between 'low' and 'high'. 'rate' is how "
        + "many new values it passes through a second: 0.05 drifts like weather, 4 wobbles. "
        + "Patch it into anything that should keep changing once the sequence has been heard "
        + "— a cutoff, a level, a hue. Whole 'seed's share nothing; two Wanders with the same "
        + "'seed' and 'rate' move together, and on the picture it is the same value at every "
        + "pixel, so the screen follows what the speakers follow.")
    {
        Skin = new ModuleSkin.Palette(CategoryAccents.Of(ModuleCategories.Oscillators))
        {
            Glyph = "M2,14 C5,6 6,18 9,10 C11,4 13,16 15,8 C17,3 19,14 22,11",
        },
    };

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var value = em.Ternary(OpCode.Noise3, node[2], node[2], em.Mul(node[0], node[1]));

        return [em.Ternary(OpCode.Mix, node[3], node[4], value)];
    }
}
