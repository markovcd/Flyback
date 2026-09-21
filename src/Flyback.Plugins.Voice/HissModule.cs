using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Voice;

/// <summary>
/// Noise through a filter, as loud as the envelope patched into it: a hi-hat, the
/// wires of a snare, a clap, a riser, wind.
/// </summary>
/// <remarks>
/// A <see cref="RandomModule"/>, a <see cref="FilterModule"/> and the Multiply that
/// plays them, which is every noise part there is. Which noise and which of the
/// filter's three responses are settings rather than sockets, because they decide
/// what is emitted: pink is thirteen lookups nobody should pay for a hat. The noise
/// has no memory, so a patch that gave several Filters one Random and now gives
/// each part a Hiss with the same seed plays the samples it played.
/// </remarks>
internal static class HissModule
{
    public const string TypeId = "flyback.voice.hiss";

    public const string StateKey = "hiss";

    public const string NoiseKey = "noise";

    public const string BandKey = "band";

    private const string White = "white";

    private const string Pink = "pink";

    private const string Low = "low";

    private const string Band = "band";

    private const string High = "high";

    public static NodeDef Definition { get; } = new(
        TypeId, "Hiss", ModuleCategories.Oscillators,
        [
            new PortSpec("in", NormalledTo: NodeCatalog.Clock, Domain: true),
            new PortSpec("level", PortKind.Scalar, 1f, 0f, 1f),
            new PortSpec("cutoff", PortKind.Scalar, 8000f, 20f, 12_000f),
            new PortSpec("resonance", PortKind.Scalar, 0.2f, 0f, 1f),
            new PortSpec("gain", PortKind.Scalar, 1f, 0f),
            new PortSpec("seed", PortKind.Scalar, 0f, 0f, 16f, Display: PortDisplay.Integer),
        ],
        [new PortSpec("out")],
        Emit,
        "A hi-hat, or a snare's wires, a clap, a riser: noise through a filter. Patch an "
        + "envelope into 'level' — a Stroke, a Decay, a Euclid's 'stroke' — and that is how loud "
        + "it is. 'cutoff' and 'resonance' are the Filter's: 8000 on the high band is a hat, "
        + "1900 on the middle one a snare, and a 'cutoff' swept upwards is a riser. 'gain' makes "
        + "up what a narrow band takes away. The noise and the band are set on the node: white "
        + "or pink, and low, band or high. Audio only, like the Filter in it.")
    {
        Extras =
        [
            new SettingsExtra(
                StateKey,
                [
                    new ExtraField.Choice(
                        NoiseKey, "noise", [new ChoiceOption(White, "White"), new ChoiceOption(Pink, "Pink")], White),
                    new ExtraField.Choice(
                        BandKey,
                        "band",
                        [new ChoiceOption(Low, "Low"), new ChoiceOption(Band, "Band"), new ChoiceOption(High, "High")],
                        High),
                ]),
        ],
        Sinks = ModuleSinks.Audio,
    };

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var settings = node.Extra<ExtraState>(StateKey);

        var (white, pink) = RandomModule.Noise(em, node[0], node[5]);
        var responses = FilterModule.Responses(em, settings?.Chosen(NoiseKey) == Pink ? pink : white, node[2], node[3]);

        var heard = settings?.Chosen(BandKey) switch
        {
            Low => responses[0],
            Band => responses[1],
            _ => responses[2],
        };

        return [em.Mul(em.Mul(node[1], heard), node[4])];
    }
}
