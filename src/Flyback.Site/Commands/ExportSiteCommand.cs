using System.CommandLine;

namespace Flyback.Site.Commands;

/// <summary>
/// Writes the .NET site's database and media folder out for the Worker, once, at the
/// move to Cloudflare. deploy/cloudflare/README.md says how the result is loaded.
/// </summary>
internal static class ExportSiteCommand
{
    public static Command Build()
    {
        var database = new Option<FileInfo>("--db") { Description = "The site's presets.db.", Required = true };
        var media = new Option<DirectoryInfo?>("--media") { Description = "The media folder render-presets wrote into." };
        var into = new Option<DirectoryInfo>("--out") { Description = "An empty folder to write rows.sql and files/ into.", Required = true };

        var command = new Command("export-site", "Write the .NET preset site's database and media out as rows for D1 and files for R2.")
        {
            database, media, into,
        };

        command.SetAction(result => Run(
            result.GetRequiredValue(database),
            result.GetValue(media),
            result.GetRequiredValue(into),
            BrowserPlugins.Linked(),
            result.InvocationConfiguration.Output,
            result.InvocationConfiguration.Error));

        return command;
    }

    internal static int Run(FileInfo database, DirectoryInfo? media, DirectoryInfo into, BrowserPlugins browser, TextWriter output, TextWriter error)
    {
        if (!database.Exists)
        {
            error.WriteLine($"flyback-site: {database.FullName}: there is no such database.");
            return Exit.Failed;
        }

        if (media is { Exists: false })
        {
            error.WriteLine($"flyback-site: {media.FullName}: there is no such folder.");
            return Exit.Failed;
        }

        if (into.Exists && into.EnumerateFileSystemInfos().Any())
        {
            error.WriteLine($"flyback-site: {into.FullName}: the folder is not empty.");
            return Exit.Failed;
        }

        var counted = SiteExport.Write(database.FullName, media?.FullName, into.FullName, browser);

        output.WriteLine($"{counted.Presets} presets, {counted.Plugins} plugins and {counted.Files} files written to {into.FullName}.");

        return Exit.Ok;
    }
}
