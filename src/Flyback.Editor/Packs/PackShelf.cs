using Flyback.Core.Graph;
using Flyback.Editor.Shell;
using Flyback.Engine.Packs;
using Flyback.Plugins.Hosting;

namespace Flyback.Editor.Packs;

/// <summary>
/// Keeps the module list in step with the packs folder while the editor runs: asked before
/// anything needs the modules, it reads the folder again when a pack file was added, removed
/// or written since it last looked (ADR-0243).
/// </summary>
internal sealed class PackShelf(PluginCatalog plugins, EditorFolders folders, ReportLine report)
{
    private string seen = InstalledPacks.Stamp(folders.PackFolder);

    /// <summary>
    /// Reads the folder again where it changed, and hands the module list the result.
    /// Packs a bundle brought stay, as do packs whose file went, so an open patch keeps its modules.
    /// </summary>
    /// <returns>Whether the folder had changed.</returns>
    public bool Refresh()
    {
        var now = InstalledPacks.Stamp(folders.PackFolder);

        if (now == seen || folders.PackFolder is not { } folder) return false;

        seen = now;

        var (modules, problems) = InstalledPacks.Into(plugins.Plain, folder);

        foreach (var kept in Packs(plugins.Modules).Where(pack => !modules.HasProvider(pack.Id)))
        {
            var (added, issues) = PackModules.Into(modules, kept.Text);

            if (issues.Count == 0) modules = added;
        }

        plugins.Take(modules);

        if (problems is [var first, ..])
            report.Say($"A pack in the packs folder has a problem: {first}", string.Join('\n', problems));

        return true;
    }

    /// <summary>
    /// Writes the pack in <paramref name="text"/> into the packs folder and takes its modules
    /// into the module list, saying what it did or why not.
    /// </summary>
    public PackInstalled? Install(string text)
    {
        if (folders.PackFolder is not { } folder)
        {
            report.Say("Pack not installed: this editor has no packs folder.");
            return null;
        }

        var (installed, problems) = InstalledPacks.Install(plugins.Plain, folder, text);

        if (installed is null)
        {
            report.Say($"Pack not installed: {problems[0]}", string.Join('\n', problems));
            return null;
        }

        Refresh();

        var names = string.Join(", ", installed.Modules.Select(id => plugins.Modules.Get(id)?.Name ?? id));

        report.Say(installed.Replaced is { } was && was != installed.Version
            ? $"Updated pack {installed.Id} from {was} to {installed.Version}: {names}."
            : $"Installed pack {installed.Id} {installed.Version}: {names}. Its modules are in the module list.");

        return installed;
    }

    private static IEnumerable<PackSource> Packs(ModuleCatalog modules) => modules.All
        .Select(PackModules.SourceOf)
        .OfType<PackSource>()
        .DistinctBy(pack => pack.Id);
}
