using System.Text.Json;
using System.Text.Json.Nodes;
using Flyback.Core.Compile;

namespace Flyback.Core.Graph;

/// <summary>
/// The notes a module carries, and how one of them is read and edited. One
/// record rather than three members on the definition, because the display and
/// range only make sense with a sequence of notes.
/// </summary>
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
    /// and snaps it with the same code every knob uses.
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
/// Passed rather than looked up, so an extra never needs the definition it hangs
/// off.
/// </param>
/// <param name="Samples">
/// Where a path is turned into audio, and null where nothing in this program can
/// open a file — a headless compile has no library.
/// </param>
/// <param name="Report">Where a complaint about this node goes.</param>
/// <param name="Pictures">
/// Where a path is turned into a picture, null the same way
/// <paramref name="Samples"/> is and additionally on every audio program.
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
/// A part of a definition rather than a subtype of one (ADR-0054): the kinds are
/// independent axes, and a hierarchy would have to name every combination. It
/// also keeps <see cref="NodeDef"/>'s constructor out of the plugin ABI, since a
/// fourth kind adds a file and no member.
/// <para>
/// A plugin may write its own, stored the way the engine's kinds are: under
/// <see cref="Key"/> in <see cref="NodeInstance.State"/> (ADR-0061). The
/// engine's four hand their shape back typed — see <see cref="StepsExtra.Of"/> —
/// where a plugin's is described by <see cref="Fields"/> and folded onto
/// <see cref="EmitContext.Extras"/>: what is uniform is the storage, not the
/// shape.
/// </para>
/// <para>
/// The editor is not here, because it needs Avalonia. A plugin's is drawn from
/// what <see cref="Fields"/> declares (ADR-0055), so no plugin ships UI and the
/// same declaration is what lets an assistant read and write the state.
/// </para>
/// </remarks>
public abstract record NodeExtra
{
    /// <summary>
    /// A short, stable word for this kind: what a plugin's state is filed under
    /// in <see cref="NodeInstance.State"/> and <see cref="EmitContext.Extras"/>,
    /// and what a saved patch names it by — so changing one changes the file
    /// format of every patch that holds the module. It also keeps "which extra is
    /// this" answerable without a type test.
    /// </summary>
    public abstract string Key { get; }

    /// <summary>
    /// The values this kind carries, described so the App can draw them and an
    /// assistant can set them. Empty for the engine's own three, which are drawn
    /// by controls written for them.
    /// </summary>
    /// <remarks>
    /// Declaring these is the whole of what a plugin has to do: everything below
    /// has a default written in terms of them, so a plugin's extra overrides
    /// <see cref="Key"/> and this and nothing else.
    /// </remarks>
    public virtual IReadOnlyList<ExtraField> Fields => [];

    /// <summary>What a freshly placed instance carries.</summary>
    /// <remarks>
    /// Seeding is here and copying is not: a copy is one deep clone of
    /// <see cref="NodeInstance.State"/>, and it has to work on a module this
    /// build has no definition for, so there would be no kind to ask.
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
    /// The files this instance names, and none for the kinds that name nothing
    /// outside the patch — what a bundle is packed from, see
    /// <see cref="PatchBundle"/>.
    /// </summary>
    /// <remarks>
    /// Asked of the kind rather than read off the node, so nothing doing the
    /// packing has to know that a Sample holds a WAV and an Image a PNG. A path
    /// as the patch stores it, which may be relative and may point at nothing:
    /// whether it can be read is the caller's question.
    /// </remarks>
    public virtual IEnumerable<string> Files(NodeInstance node) => [];

    /// <summary>
    /// Points this instance at the same files under different names, which is
    /// what packing one into a bundle and unpacking it again are.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Files"/> because the two happen at different
    /// moments: a bundle asks every node what it names, decides what to call each
    /// inside the archive, and only then tells the nodes.
    /// <paramref name="renamed"/> hands back what it was given for a path it does
    /// not know, so a kind may pass every path through without checking.
    /// </remarks>
    public virtual void Rebase(NodeInstance node, Func<string, string> renamed) { }

    /// <summary>
    /// What this instance is carrying, as prose — what an assistant reading a
    /// patch sees, and the one place it would otherwise miss: this is neither a
    /// socket nor a wire.
    /// </summary>
    public virtual string Report(NodeInstance node)
    {
        if (Fields.Count == 0) return $"{Key}: nothing.";

        var held = node.StateOf(Key);
        var written = Fields.Select(f => $"{f.Label} {f.Format(held?[f.Key])}");

        return $"{Key}: {string.Join(", ", written)}.";
    }

    /// <summary>
    /// That the module carries this at all — for the listing of what a module is,
    /// as opposed to what one instance holds.
    /// </summary>
    /// <remarks>
    /// What it carries and not how to write it: which tool writes a kind is the
    /// assistant's vocabulary, declared in a project that references this one —
    /// see <c>Assist.Vocabulary</c>, which appends that half.
    /// </remarks>
    public virtual string Announce()
    {
        var named = string.Join(", ", Fields.Select(f => f.Key));

        return $"  {Key,-6} {named} — not knobs";
    }

    /// <summary>
    /// This kind's state as it is stored: every declared field, held to what it
    /// can mean. Passing null builds the state a fresh instance carries, since
    /// "no value yet" and "a value that means nothing" are the same question.
    /// </summary>
    public JsonObject Stored(JsonNode? from)
    {
        var stored = new JsonObject();

        foreach (var field in Fields) stored[field.Key] = field.Sane(from?[field.Key]);

        return stored;
    }

    /// <summary>
    /// What a kind that keeps a shape of its own reads back out of the store, or
    /// <paramref name="fallback"/> where the file says nothing it can use.
    /// </summary>
    /// <remarks>
    /// The tolerance is the point: state is an opaque tree anybody may have typed
    /// into, so a scale written as a string has to come back as "no scale" rather
    /// than as an exception out of the middle of loading a patch.
    /// </remarks>
    private protected static T Read<T>(JsonNode? stored, T fallback)
    {
        if (stored is null) return fallback;

        try
        {
            return stored.Deserialize<T>() ?? fallback;
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    /// <summary>The other half of <see cref="Read{T}"/>, for a kind's own shape.</summary>
    private protected static JsonNode Write<T>(T value) =>
        JsonSerializer.SerializeToNode(value) ?? new JsonObject();
}

/// <summary>
/// One instance's worth of a declared extra, parsed and ready for an emit
/// function — what a plugin reads back out of <see cref="EmitContext.Extras"/>.
/// Typed at the point of use: a plugin knows its own schema, and the tolerance
/// for a file that means nothing has already been applied.
/// </summary>
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

/// <summary>The notes a sequencer plays.</summary>
public sealed record StepsExtra(StepSpec Spec) : NodeExtra
{
    /// <summary>
    /// Where a tune is filed in <see cref="NodeInstance.State"/>. A constant as
    /// well as the <see cref="Key"/> override, so <see cref="Of"/> and
    /// <see cref="Set"/> can be asked of a node with no definition to hand.
    /// </summary>
    public const string Name = "notes";

    public override string Key => Name;

    /// <summary>The tune this instance plays, and none where it carries no notes.</summary>
    public static List<Step> Of(NodeInstance node) => Read<List<Step>>(node.StateOf(Name), []);

    /// <summary>Replaces the tune outright — a list is edited whole or not at all.</summary>
    public static void Set(NodeInstance node, IEnumerable<Step> notes) =>
        node.SetState(Name, Write(notes.ToList()));

    public override void Seed(NodeInstance node) => Set(node, Spec.Default);

    /// <remarks>
    /// Held to what can actually be played on the way in, so the emit never has
    /// to defend itself against a zero length or a volume out of range.
    /// </remarks>
    public override EmitContext Fold(EmitContext ctx, NodeInstance node, ExtraEnv env) =>
        ctx with { Steps = [.. Of(node).Select(s => s.Sane())] };

    public override string Report(NodeInstance node)
    {
        if (Of(node) is not { Count: > 0 } steps) return "It has no notes.";

        var written = steps.Select(s =>
            s is { Length: 1f, Volume: 1f }
                ? Number(s.Value)
                : $"{Number(s.Value)}/{Number(s.Length)}@{Number(s.Volume)}");

        return $"Notes: {string.Join(" ", written)}.";
    }

    public override string Announce() =>
        $"  notes  a list of up to {NodeCatalog.MaxSteps} — not knobs";

    private static string Number(float value) =>
        value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>The notes of the octave a quantiser snaps to.</summary>
public sealed record ScaleExtra(IReadOnlyList<int> Default) : NodeExtra
{
    /// <inheritdoc cref="StepsExtra.Name"/>
    public const string Name = "scale";

    public override string Key => Name;

    /// <summary>
    /// The pitch classes this instance snaps to, as stored rather than tidied —
    /// <see cref="Fold"/> is where a scale is held to being one, because the
    /// keyboard has to show what was actually switched on.
    /// </summary>
    public static List<int> Of(NodeInstance node) => Read<List<int>>(node.StateOf(Name), []);

    /// <summary>
    /// Replaces the scale outright, for the reason a tune is replaced outright:
    /// a set sent whole cannot come out half applied.
    /// </summary>
    public static void Set(NodeInstance node, IEnumerable<int> classes) =>
        node.SetState(Name, Write(classes.ToList()));

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

    public override string Announce() =>
        $"  scale  which of the {Pitch.Classes} pitch classes are on — not knobs";
}

/// <summary>The audio file a player reads.</summary>
public sealed record SampleExtra : NodeExtra
{
    /// <inheritdoc cref="StepsExtra.Name"/>
    public const string Name = "file";

    public override string Key => Name;

    /// <summary>
    /// The path this instance names, and the empty string where it names none —
    /// which is also what a module that reads no file answers, since asking a
    /// Sine for its sample is a question about the wrong module.
    /// </summary>
    public static string Of(NodeInstance node) => Read(node.StateOf(Name), string.Empty);

    /// <summary>Points this instance at a file.</summary>
    public static void Set(NodeInstance node, string path) => node.SetState(Name, Write(path));

    /// <remarks>
    /// Empty rather than nothing, so a module that reads a file always has
    /// somewhere to put one and the panel always has a row to show.
    /// </remarks>
    public override void Seed(NodeInstance node) => Set(node, string.Empty);

    /// <remarks>
    /// The one extra that can fail, and the complaints are its own: what a missing
    /// file costs is a fact about this module, and nothing in the walk needs to
    /// know it.
    /// </remarks>
    public override EmitContext Fold(EmitContext ctx, NodeInstance node, ExtraEnv env)
    {
        var path = Of(node);

        if (string.IsNullOrWhiteSpace(path))
        {
            env.Report(new CompileIssue(
                node.Id,
                $"'{env.Title}' has no sound file chosen, so it plays silence. Pick one in the panel.",
                IssueSeverity.Warning));

            return ctx;
        }

        if (env.Samples?.Find(path) is { } loaded) return ctx with { Sample = loaded };

        env.Report(new CompileIssue(
            node.Id,
            $"'{env.Title}' cannot read {path} — "
            + (env.Samples?.Explain(path) ?? "nothing here can open a sound file.")
            + " A patch names its samples rather than carrying them, so this one has to be "
            + "somewhere it can be found."));

        return ctx;
    }

    public override IEnumerable<string> Files(NodeInstance node)
    {
        var path = Of(node);

        return string.IsNullOrWhiteSpace(path) ? [] : [path];
    }

    public override void Rebase(NodeInstance node, Func<string, string> renamed)
    {
        var path = Of(node);

        if (!string.IsNullOrWhiteSpace(path)) Set(node, renamed(path));
    }

    public override string Report(NodeInstance node)
    {
        var path = Of(node);

        return string.IsNullOrWhiteSpace(path)
            ? "No file chosen, so it plays silence."
            : $"File: {path}.";
    }

    public override string Announce() =>
        "  file   a path to a WAV — not a knob";
}

/// <summary>The picture a module shows.</summary>
/// <remarks>
/// <see cref="SampleExtra"/> for the other kind of file, and a separate kind
/// rather than a parameter on that one (ADR-0054): they share the shape of the
/// field and share neither the library, the fault, the sentence, nor — the one
/// that decides it — which program may read one at all.
/// </remarks>
public sealed record PictureExtra : NodeExtra
{
    /// <inheritdoc cref="StepsExtra.Name"/>
    public const string Name = "picture";

    public override string Key => Name;

    /// <inheritdoc cref="SampleExtra.Of"/>
    public static string Of(NodeInstance node) => Read(node.StateOf(Name), string.Empty);

    /// <summary>Points this instance at a picture.</summary>
    public static void Set(NodeInstance node, string path) => node.SetState(Name, Write(path));

    /// <inheritdoc cref="SampleExtra.Seed"/>
    public override void Seed(NodeInstance node) => Set(node, string.Empty);

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

        var path = Of(node);

        if (string.IsNullOrWhiteSpace(path))
        {
            env.Report(new CompileIssue(
                node.Id,
                $"'{env.Title}' has no picture chosen, so it shows black. Pick one in the panel.",
                IssueSeverity.Warning));

            return ctx;
        }

        if (library.Find(path) is { } loaded) return ctx with { Picture = loaded };

        env.Report(new CompileIssue(
            node.Id,
            $"'{env.Title}' cannot read {path} — {library.Explain(path)}"
            + " A patch names its pictures rather than carrying them, so this one has to be"
            + " somewhere it can be found."));

        return ctx;
    }

    public override IEnumerable<string> Files(NodeInstance node)
    {
        var path = Of(node);

        return string.IsNullOrWhiteSpace(path) ? [] : [path];
    }

    public override void Rebase(NodeInstance node, Func<string, string> renamed)
    {
        var path = Of(node);

        if (!string.IsNullOrWhiteSpace(path)) Set(node, renamed(path));
    }

    public override string Report(NodeInstance node)
    {
        var path = Of(node);

        return string.IsNullOrWhiteSpace(path)
            ? "No picture chosen, so it shows black."
            : $"Picture: {path}.";
    }

    public override string Announce() =>
        "  picture   a path to a PNG — not a knob";
}
