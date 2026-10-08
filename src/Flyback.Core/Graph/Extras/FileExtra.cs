namespace Flyback.Core.Graph.Extras;

/// <summary>An extra that names a file the module reads, kept as a path relative to the patch.</summary>
/// <remarks>
/// Each kind of file stays an extra of its own (ADR-0054); what they share is the path,
/// carried, rebased and reported alike, and how a person picks one.
/// </remarks>
public abstract record FileExtra : NodeExtra
{
    /// <summary>How a person picks one, and what is said of it.</summary>
    public abstract FileKind Kind { get; }

    /// <summary>The path an instance names, and the empty string where it names none.</summary>
    public string PathOf(NodeInstance node) => Read(node.StateOf(Key), string.Empty);

    /// <summary>Points an instance at a file.</summary>
    public void Point(NodeInstance node, string path) => node.SetState(Key, Write(path));

    /// <remarks>
    /// Empty rather than nothing, so a module that reads a file always has
    /// somewhere to put one and the panel always has a row to show.
    /// </remarks>
    public override void Seed(NodeInstance node) => Point(node, string.Empty);

    /// <inheritdoc/>
    public override IEnumerable<string> Files(NodeInstance node)
    {
        var path = PathOf(node);

        return string.IsNullOrWhiteSpace(path) ? [] : [path];
    }

    /// <inheritdoc/>
    public override void Rebase(NodeInstance node, Func<string, string> renamed)
    {
        var path = PathOf(node);

        if (!string.IsNullOrWhiteSpace(path)) Point(node, renamed(path));
    }

    /// <inheritdoc/>
    public override string Report(NodeInstance node)
    {
        var path = PathOf(node);

        return string.IsNullOrWhiteSpace(path) ? Kind.Unchosen : $"{Kind.Called}: {path}.";
    }
}
