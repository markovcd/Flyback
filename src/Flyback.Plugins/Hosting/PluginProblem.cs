namespace Flyback.Plugins.Hosting;

/// <summary>
/// Something that went wrong with one plugin. Collected rather than thrown: a
/// broken plugin must not stop the program starting, and the person who has to
/// fix it needs to be told which file it was.
/// </summary>
internal sealed record PluginProblem(string Source, string Message)
{
    /// <summary>The plugin folder it came from, or null where it came from no folder.</summary>
    internal string? Folder { get; init; }

    public override string ToString() => $"{Source}: {Message}";
}