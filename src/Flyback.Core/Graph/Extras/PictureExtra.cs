using Flyback.Core.Compile;

namespace Flyback.Core.Graph.Extras;

/// <summary>The picture a module shows.</summary>
/// <remarks>
/// <see cref="SampleExtra"/> for the other kind of file, and a separate kind
/// rather than a parameter on that one (ADR-0054): they share the shape of the
/// field and share neither the library, the fault, the sentence, nor — the one
/// that decides it — which program may read one at all.
/// </remarks>
public sealed record PictureExtra : FileExtra
{
    /// <inheritdoc cref="StepsExtra.Name"/>
    public const string Name = "picture";

    /// <inheritdoc/>
    public override string Key => Name;

    /// <inheritdoc/>
    public override FileKind Kind => Picker;

    private static readonly FileKind Picker = new(
        Label: "picture",
        Choose: "Choose a picture",
        Described: "PNG images",
        Patterns: ["*.png"],
        MimeTypes: ["image/png"],
        Called: "Picture",
        Unchosen: "No picture chosen, so it shows black.",
        Picture: true);

    /// <inheritdoc cref="SampleExtra.Of"/>
    public static string Of(NodeInstance node) => Read(node.StateOf(Name), string.Empty);

    /// <summary>Points this instance at a picture.</summary>
    public static void Set(NodeInstance node, string path) => node.SetState(Name, Write(path));

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

    /// <inheritdoc/>
    public override string Announce() =>
        "  picture   a path to a PNG — not a knob";

    /// <inheritdoc/>
    public override string Help => "The PNG it shows. The patch keeps its path, so moving the file breaks it.";
}
