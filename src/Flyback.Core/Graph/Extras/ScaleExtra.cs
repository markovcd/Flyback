namespace Flyback.Core.Graph.Extras;

/// <summary>The notes of the octave a quantiser snaps to.</summary>
public sealed record ScaleExtra(IReadOnlyList<int> Default) : NodeExtra
{
    /// <inheritdoc cref="StepsExtra.Name"/>
    internal const string Name = "scale";

    /// <inheritdoc/>
    public override string Key => Name;

    /// <summary>
    /// The pitch classes this instance snaps to, as stored rather than tidied —
    /// <see cref="Fold"/> is where a scale is held to being one, because the
    /// keyboard has to show what was actually switched on.
    /// </summary>
    internal static List<int> Of(NodeInstance node) => Read<List<int>>(node.StateOf(Name), []);

    /// <summary>
    /// Replaces the scale outright, for the reason a tune is replaced outright:
    /// a set sent whole cannot come out half applied.
    /// </summary>
    public static void Set(NodeInstance node, IEnumerable<int> classes) =>
        node.SetState(Name, Write(classes.ToList()));

    /// <inheritdoc/>
    public override void Seed(NodeInstance node) => Set(node, Pitch.Scale(Default));

    /// <remarks>
    /// The tidying here is load-bearing rather than defensive: a scale naming a
    /// note twice lowers to two identical candidates, and one naming a thirteenth
    /// to a candidate outside the octave. Both compile, and neither is a scale.
    /// </remarks>
    public override EmitContext Fold(EmitContext ctx, NodeInstance node, ExtraEnv env) =>
        ctx with
        {
            Scale = Of(node) is { Count: > 0 } classes ? Pitch.Scale(classes) : [],
        };

    /// <inheritdoc/>
    public override string Report(NodeInstance node)
    {
        if (Of(node) is not { Count: > 0 } scale)
            return "Its scale is empty, so it passes the signal through unchanged.";

        var named = string.Join(" ", scale.Select(Pitch.ClassName));
        var numbers = string.Join(", ", scale);

        return scale.Count == Pitch.Classes
            ? $"Scale: all twelve ({numbers}), which is the nearest semitone."
            : $"Scale: {named} ({numbers}).";
    }

    /// <inheritdoc/>
    public override string Announce() =>
        $"  scale  which of the {Pitch.Classes} pitch classes are on — not knobs";

    /// <inheritdoc/>
    public override string Help => "The notes the scale keeps. A note switched on is on in every octave.";
}