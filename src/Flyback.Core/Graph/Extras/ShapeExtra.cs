using Flyback.Core.Compile;

namespace Flyback.Core.Graph.Extras;

/// <summary>The drawing a Path plays: an SVG, an OBJ or a PNG.</summary>
internal sealed record ShapeExtra : NodeExtra
{
    /// <inheritdoc cref="StepsExtra.Name"/>
    public const string Name = "shape";

    /// <inheritdoc/>
    public override string Key => Name;

    /// <inheritdoc cref="SampleExtra.Of"/>
    public static string Of(NodeInstance node) => Read(node.StateOf(Name), string.Empty);

    /// <summary>Points this instance at a drawing.</summary>
    public static void Set(NodeInstance node, string path) => node.SetState(Name, Write(path));

    /// <inheritdoc cref="SampleExtra.Seed"/>
    public override void Seed(NodeInstance node) => Set(node, string.Empty);

    /// <inheritdoc cref="SampleExtra.Fold"/>
    public override EmitContext Fold(EmitContext ctx, NodeInstance node, ExtraEnv env)
    {
        var path = Of(node);

        if (string.IsNullOrWhiteSpace(path))
        {
            env.Report(new CompileIssue(
                node.Id,
                $"'{env.Title}' has no drawing chosen, so it stays at the center. Pick one in the panel.",
                IssueSeverity.Warning));

            return ctx;
        }

        var shapes = env.Samples as IShapeLibrary;

        if (shapes?.FindShape(path) is { } shape) return ctx with { Shape = shape };

        env.Report(new CompileIssue(
            node.Id,
            $"'{env.Title}' cannot read {path} — "
            + (shapes?.ExplainShape(path) ?? "nothing here can open a drawing.")
            + " A patch names its files rather than carrying them, so this one has to be "
            + "somewhere it can be found."));

        return ctx;
    }

    /// <inheritdoc/>
    public override IEnumerable<string> Files(NodeInstance node)
    {
        var path = Of(node);

        return string.IsNullOrWhiteSpace(path) ? [] : [path];
    }

    /// <inheritdoc/>
    public override void Rebase(NodeInstance node, Func<string, string> renamed)
    {
        var path = Of(node);

        if (!string.IsNullOrWhiteSpace(path)) Set(node, renamed(path));
    }

    /// <inheritdoc/>
    public override string Report(NodeInstance node)
    {
        var path = Of(node);

        return string.IsNullOrWhiteSpace(path)
            ? "No drawing chosen, so it stays at the center."
            : $"Drawing: {path}.";
    }

    /// <inheritdoc/>
    public override string Announce() =>
        "  shape   a path to an .svg, .obj or .png — not a knob";

    /// <inheritdoc/>
    public override string Help => "The SVG, OBJ or PNG it draws. The patch keeps its path, so moving the file breaks it.";
}
