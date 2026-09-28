using System.Text.Json;
using Flyback.Cli.Common;
using Flyback.Core;
using Flyback.Plugins.Hosting;

namespace Flyback.Cli.Commands;

/// <summary>Which plugin folders load, and saying yes or no to one (<see cref="PluginTrust"/>).</summary>
internal static class PluginCommand
{
    /// <summary>
    /// A folder given by path, or by name under <paramref name="directory"/> where no
    /// folder of that path exists here.
    /// </summary>
    public static string Resolve(string folder, string directory)
    {
        var named = Path.Combine(directory, folder);

        return !Directory.Exists(folder) && Directory.Exists(named) ? Path.GetFullPath(named) : Path.GetFullPath(folder);
    }

    public static int Allow(string folder, bool secrets, PluginAllowances allowances, TextWriter output, TextWriter error)
    {
        if (!Directory.Exists(folder) || PluginHost.EntryAssemblies(folder).Count == 0)
        {
            error.WriteLine($"{GlobalConstants.ApplicationName}: there is no plugin in {folder}.");
            return Exit.Failed;
        }

        PluginAllowance allowed;

        try
        {
            allowed = allowances.Allow(folder, secrets);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"{GlobalConstants.ApplicationName}: {allowances.File}: {ex.Message}");
            return Exit.Failed;
        }

        output.WriteLine($"Allowed {allowed.Assembly} in {allowed.Folder}, as its {Writing.Count(allowed.Files.Hashes.Count, "file")} stand now.");
        output.WriteLine(secrets
            ? "  It may keep keys. It loads at the next start."
            : "  It loads at the next start. A secret store it registers is refused without --secrets.");

        return Exit.Ok;
    }

    public static int Deny(string folder, PluginAllowances allowances, TextWriter output, TextWriter error)
    {
        bool denied;

        try
        {
            denied = allowances.Deny(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"{GlobalConstants.ApplicationName}: {allowances.File}: {ex.Message}");
            return Exit.Failed;
        }

        if (!denied)
        {
            error.WriteLine($"{GlobalConstants.ApplicationName}: nothing was allowed for {folder}.");
            return Exit.Failed;
        }

        output.WriteLine($"No longer allowed: {folder}. It is not loaded from the next start.");

        return Exit.Ok;
    }

    public static int List(string directory, PluginTrust trust, bool json, TextWriter output)
    {
        var rows = (Directory.Exists(directory) ? PluginHost.Folders(directory) : [])
            .Where(folder => PluginHost.EntryAssemblies(folder).Count > 0)
            .Select(folder => (Folder: folder, Verdict: trust.Judge(folder)))
            .ToList();

        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(
                new
                {
                    plugins = directory,
                    @checked = trust.Checks,
                    folders = rows.Select(r => new
                    {
                        name = Path.GetFileName(r.Folder),
                        folder = r.Folder,
                        standing = Word(r.Verdict.Standing).Replace(' ', '-'),
                        loads = r.Verdict.Loads,
                        secrets = r.Verdict.Loads && r.Verdict.Secrets,
                        reason = r.Verdict.Reason,
                    }),
                },
                Writing.Json));

            return Exit.Ok;
        }

        output.WriteLine($"plugins: {directory}");

        if (!trust.Checks) output.WriteLine("  This build checks nothing, and loads every folder.");

        if (rows.Count == 0) output.WriteLine("  No plugin folders.");

        var width = rows.Count == 0 ? 0 : rows.Max(r => Path.GetFileName(r.Folder).Length);

        foreach (var (folder, verdict) in rows)
        {
            var keys = verdict.Loads && verdict.Secrets ? ", may keep keys" : string.Empty;

            output.WriteLine($"  {Path.GetFileName(folder).PadRight(width)}  {Word(verdict.Standing)}{keys}");

            if (verdict.Reason is { } reason) output.WriteLine($"  {new string(' ', width)}  {reason}");
        }

        return Exit.Ok;
    }

    private static string Word(PluginStanding standing) => standing switch
    {
        PluginStanding.Unchecked => "unchecked",
        PluginStanding.Shipped => "shipped",
        PluginStanding.Allowed => "allowed",
        PluginStanding.Changed => "changed",
        _ => "not allowed",
    };
}
