namespace Flyback.Plugins.Hosting;

/// <summary>
/// The plugins this copy of Flyback was built with: <see cref="FileName"/> beside the
/// plugins folder, a line of <c>&lt;sha256&gt;  &lt;folder&gt;/&lt;path&gt;</c> for each of their files.
/// </summary>
/// <remarks>
/// Written by the build (Directory.Build.targets) and never by Flyback, and kept outside
/// <c>plugins/</c>, the folder a stranger's zip is extracted into. A release carries it
/// among the files its signed SHA256SUMS covers.
/// </remarks>
internal sealed class ShippedList
{
    public const string FileName = "plugins.sha256";

    private readonly Dictionary<string, PluginFiles> folders;

    private ShippedList(Dictionary<string, PluginFiles> folders) => this.folders = folders;

    public static ShippedList Empty { get; } = new([]);

    /// <summary>The list beside <paramref name="directory"/>, or <see cref="Empty"/> where there is none.</summary>
    public static ShippedList Beside(string directory)
    {
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)));
        var file = parent is null ? null : Path.Combine(parent, FileName);

        try
        {
            return file is not null && File.Exists(file) ? Parse(File.ReadAllText(file)) : Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Empty;
        }
    }

    public static ShippedList Parse(string text)
    {
        var files = new Dictionary<string, List<KeyValuePair<string, string>>>(StringComparer.Ordinal);

        foreach (var line in text.Split('\n'))
        {
            var parts = line.TrimEnd('\r').Split("  ", 2);

            if (parts.Length != 2 || parts[1].Split('/', 2) is not [var folder, var path]) continue;

            if (!files.TryGetValue(folder, out var list)) files[folder] = list = [];

            list.Add(new(path, parts[0]));
        }

        return new ShippedList(files.ToDictionary(f => f.Key, f => PluginFiles.From(f.Value), StringComparer.Ordinal));
    }

    /// <summary>What the folder named <paramref name="folder"/> was built with, or null where it was not.</summary>
    public PluginFiles? Of(string folder) => folders.GetValueOrDefault(folder);
}
