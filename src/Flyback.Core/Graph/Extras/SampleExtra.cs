using Flyback.Core.Compile;

namespace Flyback.Core.Graph.Extras;

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

    public override string Help => "The WAV it plays. The patch keeps its path, so moving the file breaks it.";
}