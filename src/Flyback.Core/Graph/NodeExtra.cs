using Flyback.Core.Compile;

namespace Flyback.Core.Graph;

/// <summary>
/// The notes a module carries, and how one of them is read and edited.
/// </summary>
/// <remarks>
/// One record rather than three members on the definition, because the three
/// only ever mean anything together: a display and a range describe a step, and
/// a module with no steps has nothing for them to describe. Held that way, the
/// state that used to be legal and meaningless — a range on a module that has no
/// notes — cannot be written down.
/// </remarks>
/// <param name="Default">The tune a freshly placed instance carries.</param>
/// <param name="Display">How a step's value reads: by name on the Note Sequencer.</param>
/// <param name="Range">The span a step's value is edited within.</param>
public sealed record StepSpec(
    IReadOnlyList<Step> Default,
    PortDisplay Display,
    (float Min, float Max) Range)
{
    /// <summary>
    /// A step's value described as though it were a socket, so the editor formats
    /// and snaps it with the same code every knob already uses — a note in a list
    /// reads "A3" for the same reason a note on a knob does.
    /// </summary>
    public PortSpec AsPort => new(
        "value", PortKind.Scalar, 0f, Range.Min, Range.Max, -1, Display);
}

/// <summary>
/// What a compilation lends an extra that its node cannot tell it: who is
/// asking, where to find a file, and where to put a complaint.
/// </summary>
/// <param name="Title">
/// What the node is called, resolved already — see <see cref="NodeInstance.Title"/>.
/// Passed rather than looked up so that an extra never needs the definition it
/// is hanging off.
/// </param>
/// <param name="Samples">
/// Where a path is turned into audio, and null where nothing in this program can
/// open a file. Null is not a fault: a headless compile has no library, and an
/// extra that wanted one says so in the ordinary way.
/// </param>
/// <param name="Report">Where a complaint about this node goes.</param>
public readonly record struct ExtraEnv(
    string Title,
    ISampleLibrary? Samples,
    Action<CompileIssue> Report);

/// <summary>
/// One thing an instance carries that is not a knob: a sequencer's notes, a
/// quantiser's scale, a player's file.
/// </summary>
/// <remarks>
/// A part of a definition rather than a subtype of one
/// ([0054](0054-what-a-module-carries-is-a-part-not-a-subtype.md)). Three of
/// these are independent axes, so a module may want any combination of them, and
/// a hierarchy would have to name every combination. It also keeps
/// <see cref="NodeDef"/>'s constructor out of the plugin ABI: adding a fourth
/// kind adds a file and no member, so a plugin compiled against an earlier build
/// still finds the constructor it was compiled against.
/// <para>
/// What is virtual here is exactly what lives in this assembly and applies to
/// every kind. The inspector's editor is not: it needs Avalonia, which the
/// engine does not reference, so the App maps an extra to a control of its own.
/// Nor is serialisation: the state stays in typed fields on
/// <see cref="NodeInstance"/>, so a saved patch reads as it always did and can
/// still be edited by hand.
/// </para>
/// <para>
/// Which is also the limit of this, and it is worth knowing before deriving one:
/// the kinds here are the kinds there are. A plugin may write its own and the
/// loops will call it, but <see cref="NodeInstance"/> is sealed and holds three
/// fields, <see cref="EmitContext"/> holds the matching four, and a patch is
/// serialised from the former's declared properties — so <see cref="Seed"/> and
/// <see cref="Fold"/> have nowhere to put anything new. Only
/// <see cref="Announce"/> and <see cref="Report"/> work, which makes a plugin's
/// own kind able to describe state it cannot hold. Storing in a field that
/// already exists is the one arrangement that works throughout.
/// </para>
/// </remarks>
public abstract record NodeExtra
{
    /// <summary>What a freshly placed instance carries.</summary>
    /// <remarks>
    /// Seeding is here and copying is not. Copying an instance is
    /// <see cref="NodeInstance.Clone"/>'s, because it must work on a module this
    /// build has no definition for — a fragment from a plugin that is not
    /// loaded still has to keep its notes rather than lose them quietly.
    /// </remarks>
    public abstract void Seed(NodeInstance node);

    /// <summary>
    /// Reads the state onto the context the emit function is handed, tidying it
    /// on the way — a hand-edited file is the one way an unplayable value
    /// arrives, and the emit should not have to defend itself against one.
    /// </summary>
    public abstract EmitContext Fold(EmitContext ctx, NodeInstance node, ExtraEnv env);

    /// <summary>
    /// What this instance is carrying, as prose. What an assistant reading a
    /// patch sees, and the one place it would otherwise miss: this is neither a
    /// socket nor a wire, so a listing of either shows nothing of it.
    /// </summary>
    public abstract string Report(NodeInstance node);

    /// <summary>
    /// That the module carries this at all, and how to set it — for the listing
    /// of what a module is, as opposed to what one instance holds.
    /// </summary>
    public abstract string Announce();
}

/// <summary>The notes a sequencer plays — see <see cref="NodeInstance.Steps"/>.</summary>
public sealed record StepsExtra(StepSpec Spec) : NodeExtra
{
    public override void Seed(NodeInstance node) => node.Steps = [.. Spec.Default];

    /// <remarks>
    /// Held to what can actually be played on the way in, so the emit never has
    /// to defend itself against a zero length or a volume out of range.
    /// </remarks>
    public override EmitContext Fold(EmitContext ctx, NodeInstance node, ExtraEnv env) =>
        ctx with
        {
            Steps = node.Steps is { Count: > 0 } notes ? [.. notes.Select(s => s.Sane())] : [],
        };

    public override string Report(NodeInstance node)
    {
        if (node.Steps is not { Count: > 0 } steps) return "It has no notes.";

        var written = steps.Select(s =>
            s is { Length: 1f, Volume: 1f }
                ? Number(s.Value)
                : $"{Number(s.Value)}/{Number(s.Length)}@{Number(s.Volume)}");

        return $"Notes: {string.Join(" ", written)}.";
    }

    public override string Announce() =>
        $"  notes  a list of up to {NodeCatalog.MaxSteps}, set with set_steps — not knobs";

    private static string Number(float value) =>
        value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>The notes of the octave a quantiser snaps to — see <see cref="NodeInstance.Scale"/>.</summary>
public sealed record ScaleExtra(IReadOnlyList<int> Default) : NodeExtra
{
    public override void Seed(NodeInstance node) => node.Scale = Pitch.Scale(Default);

    /// <remarks>
    /// The tidying here is load-bearing rather than defensive: a scale naming a
    /// note twice would lower to two identical candidates, and one naming a
    /// thirteenth to a candidate outside the octave. Both compile, and neither
    /// is a scale.
    /// </remarks>
    public override EmitContext Fold(EmitContext ctx, NodeInstance node, ExtraEnv env) =>
        ctx with
        {
            Scale = node.Scale is { Count: > 0 } classes ? Pitch.Scale(classes) : [],
        };

    public override string Report(NodeInstance node)
    {
        if (node.Scale is not { Count: > 0 } scale)
            return "Its scale is empty, so it passes the signal through unchanged.";

        var named = string.Join(" ", scale.Select(Pitch.ClassName));
        var numbers = string.Join(", ", scale);

        return scale.Count == Pitch.Classes
            ? $"Scale: all twelve ({numbers}), which is the nearest semitone."
            : $"Scale: {named} ({numbers}).";
    }

    public override string Announce() =>
        $"  scale  which of the {Pitch.Classes} pitch classes are on, set with set_scale — not knobs";
}

/// <summary>The audio file a player reads — see <see cref="NodeInstance.Sample"/>.</summary>
public sealed record SampleExtra : NodeExtra
{
    /// <remarks>
    /// Empty rather than null, so a module that reads a file always has
    /// somewhere to put one and the panel always has a row to show.
    /// </remarks>
    public override void Seed(NodeInstance node) => node.Sample = string.Empty;

    /// <remarks>
    /// The one extra that can fail, and the complaints are its own rather than
    /// the compiler's: what a missing file costs is a fact about this module,
    /// and nothing in the walk needs to know it.
    /// </remarks>
    public override EmitContext Fold(EmitContext ctx, NodeInstance node, ExtraEnv env)
    {
        if (string.IsNullOrWhiteSpace(node.Sample))
        {
            env.Report(new CompileIssue(
                node.Id,
                $"'{env.Title}' has no sound file chosen, so it plays silence. Pick one in the panel.",
                IssueSeverity.Warning));

            return ctx;
        }

        if (env.Samples?.Find(node.Sample) is { } loaded) return ctx with { Sample = loaded };

        env.Report(new CompileIssue(
            node.Id,
            $"'{env.Title}' cannot read {node.Sample} — "
            + (env.Samples?.Explain(node.Sample) ?? "nothing here can open a sound file.")
            + " A patch names its samples rather than carrying them, so this one has to be "
            + "somewhere it can be found."));

        return ctx;
    }

    public override string Report(NodeInstance node) =>
        string.IsNullOrWhiteSpace(node.Sample)
            ? "No file chosen, so it plays silence."
            : $"File: {node.Sample}.";

    public override string Announce() =>
        "  file   a path to a WAV, set with set_sample — not a knob";
}
