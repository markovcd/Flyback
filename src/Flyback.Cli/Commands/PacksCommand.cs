using System.CommandLine;
using System.Text.Json;
using Flyback.Cli.Common;
using Flyback.Core;
using Flyback.Engine.Packs;
using Flyback.Plugins.Hosting;
using Flyback.Site.Client;
using PluginRegistry = Flyback.Cli.Plugins;

namespace Flyback.Cli.Commands;

/// <summary>Installing a module pack into the packs folder from a file or the preset site, and saying which are there (ADR-0243).</summary>
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

        var words = new Argument<string?>("words") { Description = "Words every listed pack matches.", Arity = ArgumentArity.ZeroOrOne };

        var searchServer = Server();

        var search = new Command("search", "Say which module packs the preset site shares, each at its newest version.") { words, searchServer, json };

        search.SetAction((result, cancel) => WithSite(result.GetRequiredValue(searchServer), result.InvocationConfiguration.Error, site => Search(
            site,
            result.GetValue(words),
            result.GetValue(json),
            result.InvocationConfiguration.Output,
            result.InvocationConfiguration.Error,
            cancel)));

        var id = new Argument<string>("pack") { Description = "The pack's id, as its header names it." };

        var getServer = Server();

        var getFolder = new Option<string>("--folder")
        {
            Description = "The packs folder to use instead of this machine's.",
            DefaultValueFactory = _ => InstalledPacks.DefaultFolder,
        };

        var get = new Command("get", "Download a pack's newest version from the preset site, check it and copy it into the packs folder.")
        {
            id, getServer, getFolder, json,
        };

        get.SetAction((result, cancel) => WithSite(result.GetRequiredValue(getServer), result.InvocationConfiguration.Error, site => Get(
            site,
            plugins.Catalog.Plain,
            result.GetRequiredValue(id),
            result.GetRequiredValue(getFolder),
            result.GetValue(json),
            result.InvocationConfiguration.Output,
            result.InvocationConfiguration.Error,
            cancel)));

        var packs = new Command("packs", "Install module packs, modules written in the text, from a file or the preset site, and say which are installed.")
        {
            install, list, search, get,
        };

        // Out of the help while packs are dark, as the language guide leaves them out.
        packs.Hidden = true;

        return packs;
    }

    private static Option<string> Server() => new("--server")
    {
        Description = "The preset site, e.g. https://flybackmodular.app/.",
        Required = true,
    };

    private static async Task<int> WithSite(string server, TextWriter error, Func<PackSite, Task<int>> run)
    {
        if (SiteAddress.Parse(server, out var problem) is not { } root)
        {
            error.WriteLine($"{GlobalConstants.ApplicationName}: {problem}");
            return Exit.Failed;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(1) };

        return await run(new PackSite(http, root));
    }

    public static async Task<int> Search(PackSite site, string? words, bool json, TextWriter output, TextWriter error, CancellationToken cancel)
    {
        SitePacks found;

        try
        {
            found = await site.SearchAsync(words, 1, cancel);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            error.WriteLine($"{GlobalConstants.ApplicationName}: the preset site did not answer: {ex.Message}");
            return Exit.Failed;
        }

        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(new
            {
                total = found.Total,
                packs = found.Items.Select(p => new
                {
                    pack = p.Pack,
                    version = p.Version,
                    modules = p.Modules.Select(m => new { id = m.Id, name = m.Name }),
                    downloads = p.Downloads,
                }),
            }));

            return Exit.Ok;
        }

        output.WriteLine(found.Items.Count == 0 ? "The preset site shares no pack like that." : $"{Writing.Count(found.Total, "pack")} on the preset site:");

        foreach (var pack in found.Items) output.WriteLine($"  {pack.Pack} {pack.Version}: {string.Join(", ", pack.Modules.Select(m => m.Id))}");

        if (found.More) output.WriteLine($"  and {found.Total - found.Items.Count} more; narrow the words.");

        return Exit.Ok;
    }

    public static async Task<int> Get(
        PackSite site, Core.Graph.ModuleCatalog plain, string pack, string folder, bool json, TextWriter output, TextWriter error, CancellationToken cancel)
    {
        string text;

        try
        {
            if (await site.NewestAsync(pack, cancel) is not { } listed)
            {
                error.WriteLine($"{GlobalConstants.ApplicationName}: the preset site shares no pack {pack}.");
                return Exit.Failed;
            }

            text = await site.DownloadAsync(listed, cancel);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or InvalidDataException)
        {
            error.WriteLine($"{GlobalConstants.ApplicationName}: {pack}: {ex.Message}");
            return Exit.Failed;
        }

        return Installed(InstalledPacks.Install(plain, folder, text), pack, json, output, error);
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

        return Installed(InstalledPacks.Install(plain, folder, text), file.Name, json, output, error);
    }

    private static int Installed(
        (PackInstalled? Installed, IReadOnlyList<string> Problems) result, string named, bool json, TextWriter output, TextWriter error)
    {
        var (installed, problems) = result;

        if (installed is null)
        {
            foreach (var problem in problems) error.WriteLine($"{GlobalConstants.ApplicationName}: {named}: {problem}");
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
