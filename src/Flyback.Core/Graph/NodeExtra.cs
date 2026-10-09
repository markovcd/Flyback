using System.Text.Json;
using System.Text.Json.Nodes;
using Flyback.Core.Graph.Extras;

namespace Flyback.Core.Graph;

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

    /// <summary>
    /// What this kind is for, where it is edited by a control of its own rather
    /// than from <see cref="Fields"/>, whose fields each say what they are for.
    /// </summary>
    public virtual string Help => string.Empty;

    /// <summary>What this kind carries and what each is for: a field at a time, or the kind itself where it declares none.</summary>
    internal IEnumerable<(string Name, string Help)> Explained() =>
        Fields.Count > 0 ? Fields.Select(field => (field.Key, field.Help)) : [(Key, Help)];

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
    /// <c>PatchBundle</c>.
    /// </summary>
    /// <remarks>
    /// Asked of the kind rather than read off the node, so nothing doing the
    /// packing has to know that a Sample holds a sound file and an Image a PNG. A path
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