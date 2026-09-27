using Flyback.Core.Compile;

namespace Flyback.Core.Graph.Extras;

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

    public override string Help => "The PNG it shows. The patch keeps its path, so moving the file breaks it.";
}