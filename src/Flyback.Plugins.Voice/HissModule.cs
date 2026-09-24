using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins;

namespace Flyback.Plugins.Voice;

/// <summary>
/// Noise through a filter, as loud as the envelope patched into it: a hi-hat, the
/// wires of a snare, a clap, a riser, wind.
/// </summary>
/// <remarks>
/// The engine's own Noise and Filter, and the Multiply that plays them, which is
/// every noise part there is. Which noise and which of the filter's three responses
/// are settings rather than sockets, because they decide what is emitted: pink is
/// thirteen lookups nobody should pay for a hat. The noise has no memory, so a patch
/// that gave several Filters one Noise and now gives each part a Hiss with the same
/// seed plays the samples it played.
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
            new PortSpec("in", NormalledTo: NodeCatalog.Clock, Domain: true) { Help = SocketHelp.Domain },
            new PortSpec("level", PortKind.Scalar, 1f, 0f, 1f)
            {
                Help = "An envelope: a Stroke, a Decay, a Euclid's 'stroke'.",
            },
            new PortSpec("cutoff", PortKind.Scalar, 8000f, 20f, 12_000f)
            {
                Knee = 20f,
                Help = "The filter's corner, in hertz. 8000 on the high band is a hat, 1900 on the "
                    + "band a snare, and swept upwards a riser.",
            },
            new PortSpec("resonance", PortKind.Scalar, 0.2f, 0f, 1f) { Help = SocketHelp.Resonance },
            new PortSpec("gain", PortKind.Scalar, 1f, 0f) { Help = "Makes up what a narrow band takes away." },
            new PortSpec("seed", PortKind.Scalar, 0f, 0f, 16f, Display: PortDisplay.Integer) { Help = SocketHelp.Seed },
        ],
        [new PortSpec("out") { Help = "The filtered noise, times 'level'." }],
        Emit,
        "A hi-hat, a snare's wires, a clap, a riser: noise through a filter. White or pink, "
        + "and low, band or high, are set on the node. Audio only.")
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
        Skin = new ModuleSkin.Palette(CategoryAccents.Of(ModuleCategories.Oscillators))
        {
            Glyph = "M2,12 L4,8 L5,15 L7,6 L9,17 L11,9 L13,14 L15,7 L17,16 L19,10 L21,13 L22,11",
        },
    };

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var settings = node.Extra<ExtraState>(StateKey);

        var (white, pink) = NodeCatalog.WhiteAndPink(em, node[0], node[5]);
        var responses = NodeCatalog.FilterResponses(em, settings?.Chosen(NoiseKey) == Pink ? pink : white, node[2], node[3]);

        var heard = settings?.Chosen(BandKey) switch
        {
            Low => responses[0],
            Band => responses[1],
            _ => responses[2],
        };

        return [em.Mul(em.Mul(node[1], heard), node[4])];
    }
}
