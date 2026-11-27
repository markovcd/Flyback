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

    /// <summary>
    /// Writes the pack in <paramref name="text"/> into <paramref name="folder"/> as
    /// <c>&lt;id&gt;.fbks</c>, over any earlier version of it, once it reads cleanly against
    /// <paramref name="modules"/>, the catalog without packs.
    /// </summary>
    /// <returns>What was installed, or what stopped it.</returns>
    public static (PackInstalled? Installed, IReadOnlyList<string> Problems) Install(ModuleCatalog modules, string folder, string text)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(text) > MostBytes) return (null, [$"it is over {MostBytes / 1024} KB."]);

        var (added, issues) = PackModules.Into(modules, text);

        if (issues.Count > 0) return (null, [.. issues.Select(issue => $"line {issue.Line}: {issue.Message}")]);

        var offered = added.All.Where(def => modules.Get(def.TypeId) is null).ToList();

        if (offered.Count == 0 || PackModules.SourceOf(offered[0]) is not { } pack)
            return (null, ["it is not a pack: it opens with no 'pack <id> <version>' line, or offers no module."]);

        var file = Path.Combine(folder, pack.Id + Extension);
        var replaced = File.Exists(file) && Read(file) is { } before ? VersionOf(modules, before) : null;

        if (replaced is null && Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*" + Extension).Count() >= MostPacks)
            return (null, [$"the packs folder already holds {MostPacks}, the most it loads."]);

        try
        {
            Directory.CreateDirectory(folder);

            var writing = file + ".part";
            File.WriteAllText(writing, text);
            File.Move(writing, file, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, [$"it could not be written to the packs folder: {ex.Message}"]);
        }

        return (new PackInstalled(pack.Id, pack.Version, replaced, [.. offered.Select(def => def.TypeId)], file), []);
    }

    private static string? VersionOf(ModuleCatalog modules, string text)
    {
        var (added, issues) = PackModules.Into(modules, text);

        return issues.Count > 0 ? null : added.All.Select(PackModules.SourceOf).OfType<PackSource>().FirstOrDefault()?.Version;
    }

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
