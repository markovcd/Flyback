using Flyback.Core;
using Flyback.Core.Graph;
using Flyback.Engine.Packs;

namespace Flyback.Plugins.Hosting;

/// <summary>
/// The module packs installed on this machine: each <c>.fbks</c> in one folder, its modules
/// added to the catalog under the pack's id (ADR-0241). A pack is text, so nobody has to
/// allow it and it loads on every platform.
/// </summary>
internal static class InstalledPacks
{
    /// <summary>Where a desktop keeps its packs, beside its plugins.</summary>
    public static string DefaultFolder => Path.Combine(GlobalConstants.DataFolder, "packs");

    /// <summary>What a pack file is called.</summary>
    public const string Extension = ".fbks";

    /// <summary>The longest pack file read; a pack is a page or two of text.</summary>
    public const long MostBytes = 1 << 20;

    /// <summary>The most pack files one folder loads.</summary>
    public const int MostPacks = 256;

    /// <summary>
    /// <paramref name="catalog"/> with the modules of every pack in <paramref name="folder"/>, in
    /// file-name order. A pack with a mistake adds nothing and says where, as a plugin's problem.
    /// </summary>
    public static PluginCatalog Into(PluginCatalog catalog, string? folder)
    {
        if (folder is null || !Directory.Exists(folder)) return catalog;

        var (modules, problems) = Into(catalog.Modules, folder);

        return catalog.With(modules, problems);
    }

    /// <summary><paramref name="modules"/> with every pack in <paramref name="folder"/>, and what was wrong with any.</summary>
    public static (ModuleCatalog Modules, IReadOnlyList<PluginProblem> Problems) Into(ModuleCatalog modules, string folder)
    {
        var problems = new List<PluginProblem>();

        if (!Directory.Exists(folder)) return (modules, problems);

        var files = Directory.EnumerateFiles(folder, "*" + Extension)
            .Where(file => !Path.GetFileName(file).StartsWith('.'))
            .Order(StringComparer.Ordinal)
            .ToList();

        foreach (var file in files.Skip(MostPacks))
            problems.Add(Problem(file, folder, $"not loaded: a folder loads at most {MostPacks} packs."));

        foreach (var file in files.Take(MostPacks))
        {
            if (Read(file) is not { } source)
            {
                problems.Add(Problem(file, folder, $"not loaded: it could not be read, or is over {MostBytes / 1024} KB."));
                continue;
            }

            var (added, issues) = PackModules.Into(modules, source);

            if (issues.Count == 0) modules = added;
            else problems.AddRange(issues.Select(issue => Problem(file, folder, $"line {issue.Line}: {issue.Message}")));
        }

        return (modules, problems);
    }

    /// <summary>
    /// The name, size and time of every pack file in <paramref name="folder"/>, one string that
    /// changes when any file is added, removed or written.
    /// </summary>
    public static string Stamp(string? folder) =>
        folder is null || !Directory.Exists(folder)
            ? ""
            : string.Join('\n', new DirectoryInfo(folder).EnumerateFiles("*" + Extension)
                .OrderBy(file => file.Name, StringComparer.Ordinal)
                .Select(file => $"{file.Name}|{file.Length}|{file.LastWriteTimeUtc.Ticks}"));

    private static PluginProblem Problem(string file, string folder, string message) =>
        new(Path.GetFileName(file), message) { Folder = folder };

    private static string? Read(string file)
    {
        try
        {
            return new FileInfo(file).Length > MostBytes ? null : File.ReadAllText(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
