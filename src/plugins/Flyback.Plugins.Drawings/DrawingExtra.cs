using System.Runtime.CompilerServices;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;

namespace Flyback.Plugins.Drawings;

/// <summary>The drawing a Path plays: an SVG, an OBJ or a PNG, read into one closed path.</summary>
/// <remarks>
/// The host reads the file and keeps it; the parse is kept against what the host
/// answered, so an edit recompiles without reading or parsing again, and a file the
/// host forgets is parsed afresh.
/// </remarks>
internal sealed record DrawingExtra : FileExtra
{
    /// <summary>What this is filed under, in a saved patch and on a context.</summary>
    public const string Name = "drawing";

    private static readonly ConditionalWeakTable<object, Parsed> Parses = [];

    /// <inheritdoc/>
    public override string Key => Name;

    /// <inheritdoc/>
    public override FileKind Kind { get; } = new(
        Label: "drawing",
        Choose: "Choose a drawing",
        Described: "SVG, OBJ or PNG drawings",
        Patterns: ["*.svg", "*.obj", "*.png"],
        MimeTypes: ["image/svg+xml", "model/obj", "image/png"],
        Called: "Drawing",
        Unchosen: "No drawing chosen, so it stays at the center.");

    /// <inheritdoc/>
    public override EmitContext Fold(EmitContext ctx, NodeInstance node, ExtraEnv env)
    {
        var path = PathOf(node);

        if (string.IsNullOrWhiteSpace(path))
        {
            env.Report(new CompileIssue(
                node.Id,
                $"'{env.Title}' has no drawing chosen, so it stays at the center. Pick one in the panel.",
                IssueSeverity.Warning));

            return ctx;
        }

        var (shape, why) = Read(path, env.Samples);

        if (shape is not null) return ctx.With(Name, shape);

        env.Report(new CompileIssue(
            node.Id,
            $"'{env.Title}' cannot read {path} — {why} A patch names its files rather than carrying them, "
            + "so this one has to be somewhere it can be found."));

        return ctx;
    }

    /// <summary>The drawing a path names, parsed once for what the host answered with.</summary>
    private static (LoadedShape? Shape, string Why) Read(string path, ISampleLibrary? library)
    {
        if (library is null) return (null, "nothing here can open a drawing.");

        object? found = ShapeReader.IsPicture(path) ? library.FindPicture(path) : library.FindFile(path);

        if (found is null) return (null, library.ExplainFile(path));

        var parsed = Parses.GetValue(found, read =>
        {
            var shape = read is LoadedImage image
                ? ShapeReader.Read(image, out var fault)
                : ShapeReader.Read((byte[])read, path, out fault);

            return new Parsed(shape, ShapeReader.Explain(fault));
        });

        return (parsed.Shape, parsed.Why);
    }

    /// <inheritdoc/>
    public override string Announce() =>
        "  drawing   a path to an .svg, .obj or .png — not a knob";

    /// <inheritdoc/>
    public override string Help => "The SVG, OBJ or PNG it draws. The patch keeps its path, so moving the file breaks it.";

    private sealed record Parsed(LoadedShape? Shape, string Why);
}
