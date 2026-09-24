namespace Flyback.Core.Graph;

public partial class NodeCatalog
{
    public const string SendTypeId = "bus.send";
    public const string ReceiveTypeId = "bus.receive";

    /// <summary>What a Send and a Receive file their bus under.</summary>
    private const string BusKey = "bus";

    private const string BusField = "bus";

    /// <summary>What a fresh Send and a fresh Receive are set to, so the first two placed are already a pair.</summary>
    private const string FirstBus = "bus";

    private static readonly SettingsExtra BusExtra =
        new(BusKey, [new ExtraField.Text(BusField, "bus", FirstBus)]);

    /// <summary>The bus a Send or a Receive is on, trimmed; null for any other module.</summary>
    public static string? BusOf(NodeInstance node) =>
        node.TypeId is SendTypeId or ReceiveTypeId
            ? ((ExtraField.Text)BusExtra.Fields[0]).Value(node.StateOf(BusKey)?[BusField]).Trim()
            : null;

    /// <summary>Puts a Send or a Receive on <paramref name="bus"/>.</summary>
    internal static void PutOnBus(NodeInstance node, string bus)
    {
        var held = BusExtra.Stored(node.StateOf(BusKey));
        var field = BusExtra.Fields[0];

        held[BusField] = field.Sane(System.Text.Json.Nodes.JsonValue.Create(bus));
        node.SetState(BusKey, held);
    }

    /// <remarks>
    /// A wire with no cable: whatever is patched into a Send comes out of every
    /// Receive on the same bus. See <see cref="Graph.Buses"/> for how it is compiled.
    /// </remarks>
    private static IEnumerable<NodeDef> Bus()
    {
        yield return new NodeDef(
            SendTypeId, "Send", ModuleCategories.Routing,
            [Any("in")],
            [Any("out") with { Help = "The same as 'in', so a Send can sit in a chain." }],
            (_, i) => [i[0]],
            "Puts 'in' on a bus, for a Receive on the same bus to play anywhere in the patch "
            + "without a wire across it.")
        {
            Extras = [BusExtra],
        };

        yield return new NodeDef(
            ReceiveTypeId, "Receive", ModuleCategories.Routing,
            [],
            [Any("out")],
            (em, _) => [em.Constant(0f)],
            "Whatever the Send on the same bus is carrying. Any number of Receives may listen to "
            + "one Send.")
        {
            Extras = [BusExtra],
        };
    }
}
