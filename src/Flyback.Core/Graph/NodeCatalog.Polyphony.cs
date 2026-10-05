using Flyback.Core.Graph.Extras;

namespace Flyback.Core.Graph;

public partial class NodeCatalog
{
    /// <summary>The module that says which voice of a polyphonic wire is playing.</summary>
    internal const string VoiceTypeId = "poly.voice";

    /// <summary>The module that adds a polyphonic wire's voices into one.</summary>
    internal const string MergeTypeId = "poly.merge";

    /// <summary>The module that turns one wire into a polyphonic one, a step apart per voice.</summary>
    internal const string SpreadTypeId = "poly.spread";

    private const string VoicesKey = "voices";

    private static readonly SettingsExtra VoicesExtra = new(
        VoicesKey,
        [
            new ExtraField.Number(
                VoicesKey,
                "voices",
                new PortSpec("voices", PortKind.Scalar, 4f, 1f, VoiceCounts.Most, -1, PortDisplay.Integer))
            {
                Help = "How many voices its wire carries.",
            },
        ]);

    /// <summary>How many voices a placed Voice or Spread starts its wire with.</summary>
    private static int VoicesOf(NodeInstance node) =>
        (int)((ExtraField.Number)VoicesExtra.Fields[0]).Value(node.StateOf(VoicesKey)?[VoicesKey]);

    private static IEnumerable<NodeDef> Polyphony()
    {
        yield return new NodeDef(
            VoiceTypeId, "Voice", ModuleCategories.Sources,
            [],
            [
                new PortSpec("index", PortKind.Scalar, 0f, 0f, VoiceCounts.Most - 1, -1, PortDisplay.Integer)
                {
                    Help = "Which voice this is, from 0: a different number down each voice of the wire.",
                },
                new PortSpec("count", PortKind.Scalar, 4f, 1f, VoiceCounts.Most, -1, PortDisplay.Integer)
                {
                    Help = "How many voices it starts, the same on every one.",
                },
            ],
            (em, node) =>
            [
                em.Constant(node.Voice),
                em.Constant(node.Extra<ExtraState>(VoicesKey)?.Number(VoicesKey) ?? 1f),
            ],
            "A polyphonic wire whose voices are numbered 0, 1, 2 and up. Patched into a pitch, a seed "
            + "or a hue, it makes each voice of a chain a little different.")
        {
            Extras = [VoicesExtra],
            StartsVoices = VoicesOf,
        };

        yield return new NodeDef(
            MergeTypeId, "Merge", ModuleCategories.Routing,
            [Any("in") with { Help = "A polyphonic wire, or any other." }],
            [Any("out") with { Help = "Every voice of 'in' added into one." }],
            (_, i) => [i[0]],
            "Adds the voices of a polyphonic wire into one ordinary wire. The Output does the same "
            + "by itself, so a Merge is for a chord that goes on through one chain.")
        {
            MergesVoices = true,
        };

        yield return new NodeDef(
            SpreadTypeId, "Spread", ModuleCategories.Routing,
            [
                Any("in") with { Help = "What every voice starts from." },
                Num("offset", 0f, -24f, 24f) with
                {
                    Help = "Added once more on each voice: voice 0 carries 'in', voice 1 'in' plus 'offset', "
                        + "voice 2 'in' plus twice it.",
                },
            ],
            [Any("out") with { Help = "A polyphonic wire, one step of 'offset' apart per voice." }],
            (em, node) => [em.Add(node[0], em.Mul(node[1], node.Voice))],
            "Turns one wire into a polyphonic one, each voice 'offset' further on: a stack of notes "
            + "from one pitch, or a spread of detuned copies.")
        {
            Extras = [VoicesExtra],
            StartsVoices = VoicesOf,
        };
    }
}
