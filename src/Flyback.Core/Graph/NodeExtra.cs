using System.Text.Json.Nodes;
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
/// <param name="Pictures">
/// Where a path is turned into a picture, and null where nothing in this program
/// can open one. Null the same way <paramref name="Samples"/> is null and for
/// the same reasons — and additionally on every audio program, since a picture
/// is a thing to look at and the speakers have nothing to do with one.
/// </param>
public readonly record struct ExtraEnv(
    string Title,
    ISampleLibrary? Samples,
    Action<CompileIssue> Report,
    IImageLibrary? Pictures = null);

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
/// A plugin may write its own. The engine's three store in typed fields on
/// <see cref="NodeInstance"/>, which is why a saved patch reads as it always
/// did; a plugin's stores under <see cref="Key"/> in
/// <see cref="NodeInstance.State"/> and folds onto
/// <see cref="EmitContext.Extras"/>, because that class is sealed and a plugin
/// cannot add a field to it.
/// </para>
/// <para>
/// What is not here is the editor: it needs Avalonia, which the engine does not
/// reference. The engine's three are drawn by controls the App holds, and a
/// plugin's is drawn from what <see cref="Fields"/> declares
/// ([0055](0055-a-plugins-extra-declares-its-editor.md)) — so no plugin ships
/// UI, and the same declaration is what lets an assistant read and write the
/// state without a tool written for it.
/// </para>
/// </remarks>
public abstract record NodeExtra
{
    /// <summary>
    /// A short, stable word for this kind. What a plugin's state is filed under
    /// in <see cref="NodeInstance.State"/> and <see cref="EmitContext.Extras"/>,
    /// and what a saved patch names it by — so changing one is a change to the
    /// file format of every patch that holds the module.
    /// </summary>
    /// <remarks>
    /// The engine's own three carry one too, though they store in typed fields
    /// rather than by key. It is what a listing calls them, and it keeps "which
    /// extra is this" answerable without a type test.
    /// </remarks>
    public abstract string Key { get; }

    /// <summary>
    /// The values this kind carries, described so that the App can draw them and
    /// an assistant can set them. Empty for the engine's own three, which are
    /// drawn by controls written for them.
    /// </summary>
    /// <remarks>
    /// Declaring these is the whole of what a plugin has to do: everything below
    /// has a default written in terms of them, so a plugin's extra overrides
    /// <see cref="Key"/> and this and nothing else. A kind that declares none and
    /// overrides nothing carries nothing, which is a legal and useless module —
    /// so the emptiness is not defended against here.
    /// </remarks>
    public virtual IReadOnlyList<ExtraField> Fields => [];

    /// <summary>What a freshly placed instance carries.</summary>
    /// <remarks>
    /// Seeding is here and copying is not. Copying an instance is
    /// <see cref="NodeInstance.Clone"/>'s, because it must work on a module this
    /// build has no definition for — a fragment from a plugin that is not
    /// loaded still has to keep its notes rather than lose them quietly.
    /// </remarks>
    public virtual void Seed(NodeInstance node)
    {
        if (Fields.Count == 0) return;

        node.SetState(Key, Stored(null));
    }

    /// <summary>
    /// Reads the state onto the context the emit function is handed, tidying it
    /// on the way — a hand-edited file is the one way an unplayable value
    /// arrives, and the emit should not have to defend itself against one.
    /// </summary>
    public virtual EmitContext Fold(EmitContext ctx, NodeInstance node, ExtraEnv env) =>
        Fields.Count == 0 ? ctx : ctx.With(Key, new ExtraState(Fields, node.StateOf(Key)));

    /// <summary>
    /// What this instance is carrying, as prose. What an assistant reading a
    /// patch sees, and the one place it would otherwise miss: this is neither a
    /// socket nor a wire, so a listing of either shows nothing of it.
    /// </summary>
    public virtual string Report(NodeInstance node)
    {
        if (Fields.Count == 0) return $"{Key}: nothing.";

        var held = node.StateOf(Key);
        var written = Fields.Select(f => $"{f.Label} {f.Format(held?[f.Key])}");

        return $"{Key}: {string.Join(", ", written)}.";
    }

    /// <summary>
    /// That the module carries this at all, and how to set it — for the listing
    /// of what a module is, as opposed to what one instance holds.
    /// </summary>
    public virtual string Announce()
    {
        var named = string.Join(", ", Fields.Select(f => f.Key));

        return $"  {Key,-6} {named}, set with set_extra — not knobs";
    }

    /// <summary>
    /// This kind's state as it is stored: every declared field, held to what it
    /// can mean. Passing null builds the state a fresh instance carries, because
    /// "no value yet" and "a value that means nothing" are the same question.
    /// </summary>
    public JsonObject Stored(JsonNode? from)
    {
        var stored = new JsonObject();

        foreach (var field in Fields) stored[field.Key] = field.Sane(from?[field.Key]);

        return stored;
    }
}

/// <summary>
/// One instance's worth of a declared extra, parsed and ready for an emit
/// function — what a plugin reads back out of
/// <see cref="EmitContext.Extras"/>.
/// </summary>
/// <remarks>
/// Typed at the point of use, which is the whole reason this exists rather than
/// the emit function being handed the JSON. A plugin knows its own schema, so
/// asking for a field by name and getting a <c>float</c> is the natural reading,
/// and the tolerance for a file that means nothing has already been applied by
/// the time anything here is called.
/// </remarks>
public sealed class ExtraState(IReadOnlyList<ExtraField> fields, JsonNode? stored)
{
    /// <summary>What a number field holds, or its default where nothing sensible does.</summary>
    public float Number(string key) =>
        Field(key) is ExtraField.Number field ? field.Value(stored?[key]) : 0f;

    /// <summary>What a toggle field holds, or its default where nothing sensible does.</summary>
    public bool Toggle(string key) =>
        Field(key) is ExtraField.Toggle field && field.Value(stored?[key]);

    /// <summary>
    /// Which option a choice field holds, or its fallback where nothing sensible
    /// does — and the empty string for a key that is not a choice at all.
    /// </summary>
    public string Chosen(string key) =>
        Field(key) is ExtraField.Choice field ? field.Value(stored?[key]) : string.Empty;

    private ExtraField? Field(string key) => fields.FirstOrDefault(f => f.Key == key);
}

/// <summary>The notes a sequencer plays — see <see cref="NodeInstance.Steps"/>.</summary>
public sealed record StepsExtra(StepSpec Spec) : NodeExtra
{
    public override string Key => "notes";

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
    public override string Key => "scale";

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
    public override string Key => "file";

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

/// <summary>The picture a module shows — see <see cref="NodeInstance.Picture"/>.</summary>
/// <remarks>
/// <see cref="SampleExtra"/> for the other kind of file, and written as a
/// separate kind rather than as a parameter on that one for the reason
/// [0054](0054-what-a-module-carries-is-a-part-not-a-subtype.md) made kinds
/// parts: what they share is the shape of the field, and what they do not share
/// is the library, the fault, the sentence and — the one that decides it — which
/// program is allowed to read one at all. A clip is read by the speakers and a
/// picture by the screen, so the two are never even asked the same question.
/// </remarks>
public sealed record PictureExtra : NodeExtra
{
    public override string Key => "picture";

    /// <inheritdoc cref="SampleExtra.Seed"/>
    public override void Seed(NodeInstance node) => node.Picture = string.Empty;

    /// <remarks>
    /// Silent on the audio path, and that is the whole of how this module knows
    /// which program it is in: the compiler hands a picture library to the walk
    /// that draws and nothing at all to the walk that plays. So the speakers get
    /// no picture, no complaint about a file they were never going to show, and
    /// no file opened on their behalf.
    /// </remarks>
    public override EmitContext Fold(EmitContext ctx, NodeInstance node, ExtraEnv env)
    {
        if (env.Pictures is not { } library) return ctx;

        if (string.IsNullOrWhiteSpace(node.Picture))
        {
            env.Report(new CompileIssue(
                node.Id,
                $"'{env.Title}' has no picture chosen, so it shows black. Pick one in the panel.",
                IssueSeverity.Warning));

            return ctx;
        }

        if (library.Find(node.Picture) is { } loaded) return ctx with { Picture = loaded };

        env.Report(new CompileIssue(
            node.Id,
            $"'{env.Title}' cannot read {node.Picture} — {library.Explain(node.Picture)}"
            + " A patch names its pictures rather than carrying them, so this one has to be"
            + " somewhere it can be found."));

        return ctx;
    }

    public override string Report(NodeInstance node) =>
        string.IsNullOrWhiteSpace(node.Picture)
            ? "No picture chosen, so it shows black."
            : $"Picture: {node.Picture}.";

    public override string Announce() =>
        "  picture   a path to a PNG, set with set_picture — not a knob";
}
