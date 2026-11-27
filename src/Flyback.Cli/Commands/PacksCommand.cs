using System.CommandLine;
using System.Text.Json;
using Flyback.Cli.Common;
using Flyback.Core;
using Flyback.Engine.Packs;
using Flyback.Plugins.Hosting;
using PluginRegistry = Flyback.Cli.Plugins;

namespace Flyback.Cli.Commands;

/// <summary>Installing a module pack into the packs folder, and saying which are there (ADR-0243).</summary>
internal static class PacksCommand
{
    public static Command Build(PluginRegistry plugins, Option<bool> json)
    {
        var folder = new Option<string>("--folder")
        {
            Description = "The packs folder to use instead of this machine's.",
            DefaultValueFactory = _ => InstalledPacks.DefaultFolder,
        };

        var file = new Argument<FileInfo>("file") { Description = $"The pack's {InstalledPacks.Extension} file." };

        var install = new Command("install", "Check a pack and copy it into the packs folder, over any earlier version of it.") { file, folder, json };

        install.SetAction(result => Install(
            plugins.Catalog.Plain,
            result.GetRequiredValue(file),
            result.GetRequiredValue(folder),
            result.GetValue(json),
            result.InvocationConfiguration.Output,
            result.InvocationConfiguration.Error));

        var listed = new Option<string>("--folder")
        {
            Description = "The packs folder to read instead of this machine's.",
            DefaultValueFactory = _ => InstalledPacks.DefaultFolder,
        };

        var list = new Command("list", "Say which packs the packs folder holds, their modules, and what is wrong with any.") { listed, json };

        list.SetAction(result => List(
            plugins.Catalog.Plain,
            result.GetRequiredValue(listed),
            result.GetValue(json),
            result.InvocationConfiguration.Output));

        var packs = new Command("packs", "Install module packs, modules written in the text, and say which are installed.")
        {
            install, list,
        };

        // Out of the help while packs are dark, as the language guide leaves them out.
        packs.Hidden = true;

        return packs;
    }

    public static int Install(Core.Graph.ModuleCatalog plain, FileInfo file, string folder, bool json, TextWriter output, TextWriter error)
    {
        string text;

        try
        {
            if (file.Length > InstalledPacks.MostBytes)
            {
                error.WriteLine($"{GlobalConstants.ApplicationName}: {file.Name} is over {InstalledPacks.MostBytes / 1024} KB.");
                return Exit.Failed;
            }

            text = File.ReadAllText(file.FullName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"{GlobalConstants.ApplicationName}: {file.Name}: {ex.Message}");
            return Exit.Failed;
        }

        var (installed, problems) = InstalledPacks.Install(plain, folder, text);

        if (installed is null)
        {
            foreach (var problem in problems) error.WriteLine($"{GlobalConstants.ApplicationName}: {file.Name}: {problem}");
            return Exit.Failed;
        }

        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(new
            {
                id = installed.Id,
                version = installed.Version,
                replaced = installed.Replaced,
                modules = installed.Modules,
                file = installed.File,
            }));
        }
        else
        {
            output.WriteLine(installed.Replaced is { } was
                ? $"Installed {installed.Id} {installed.Version} over {was}: {string.Join(", ", installed.Modules)}."
                : $"Installed {installed.Id} {installed.Version}: {string.Join(", ", installed.Modules)}.");
            output.WriteLine($"  {installed.File}");
        }

        return Exit.Ok;
    }

    public static int List(Core.Graph.ModuleCatalog plain, string folder, bool json, TextWriter output)
    {
        var (modules, problems) = InstalledPacks.Into(plain, folder);

        var packs = modules.All
            .Where(def => plain.Get(def.TypeId) is null)
            .Select(def => (Def: def, Pack: PackModules.SourceOf(def)))
            .Where(row => row.Pack is not null)
            .GroupBy(row => row.Pack!.Id)
            .Select(group => (Id: group.Key, group.First().Pack!.Version, Modules: group.Select(row => row.Def.TypeId).ToList()))
            .ToList();

        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(new
            {
                folder,
                packs = packs.Select(p => new { id = p.Id, version = p.Version, modules = p.Modules }),
                problems = problems.Select(p => new { file = p.Source, message = p.Message }),
            }));

            return Exit.Ok;
        }

        output.WriteLine(packs.Count == 0 ? $"No packs in {folder}." : $"{Writing.Count(packs.Count, "pack")} in {folder}:");

        foreach (var (id, version, ids) in packs) output.WriteLine($"  {id} {version}: {string.Join(", ", ids)}");
        foreach (var problem in problems) output.WriteLine($"  {problem.Source}: {problem.Message}");

        return Exit.Ok;
    }
}
