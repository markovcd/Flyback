namespace Flyback.Core.Graph.Extras;

/// <summary>The notes a sequencer plays.</summary>
public sealed record StepsExtra(StepSpec Spec) : NodeExtra
{
    /// <summary>
    /// Where a tune is filed in <see cref="NodeInstance.State"/>. A constant as
    /// well as the <see cref="Key"/> override, so <see cref="Of"/> and
    /// <see cref="Set"/> can be asked of a node with no definition to hand.
    /// </summary>
    internal const string Name = "notes";

    /// <inheritdoc/>
    public override string Key => Name;

    /// <summary>The tune this instance plays, and none where it carries no notes.</summary>
    internal static List<Step> Of(NodeInstance node) => Read<List<Step>>(node.StateOf(Name), []);

    /// <summary>Replaces the tune outright — a list is edited whole or not at all.</summary>
    public static void Set(NodeInstance node, IEnumerable<Step> notes) =>
        node.SetState(Name, Write(notes.ToList()));

    /// <inheritdoc/>
    public override void Seed(NodeInstance node) => Set(node, Spec.Default);

    /// <remarks>
    /// Held to what can actually be played on the way in, so the emit never has
    /// to defend itself against a zero length or a volume out of range.
    /// </remarks>
    public override EmitContext Fold(EmitContext ctx, NodeInstance node, ExtraEnv env) =>
        ctx with { Steps = [.. Of(node).Select(s => s.Sane())] };

    /// <inheritdoc/>
    public override string Report(NodeInstance node)
    {
        if (Of(node) is not { Count: > 0 } steps) return "It has no notes.";

        var written = steps.Select(s =>
            s is { Length: 1f, Volume: 1f }
                ? Number(s.Value)
                : $"{Number(s.Value)}/{Number(s.Length)}@{Number(s.Volume)}");

        return $"Notes: {string.Join(" ", written)}.";
    }

    /// <inheritdoc/>
    public override string Announce() =>
        $"  notes  a list of up to {NodeCatalog.MaxSteps} — not knobs";

    /// <inheritdoc/>
    public override string Help => "The steps it plays in turn: add, remove and reorder them here.";

    private static string Number(float value) =>
        value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}