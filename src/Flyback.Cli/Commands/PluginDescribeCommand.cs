using System.Security.Cryptography;
using System.Text.Json;
using Flyback.Cli.Common;
using Flyback.Core;
using Flyback.Plugins.Hosting;

namespace Flyback.Cli.Commands;

/// <summary>What a plugin package says it is and what its code names, read as the install dialog reads it, with none of it run.</summary>
internal static class PluginDescribeCommand
{
    public static int Run(FileInfo file, bool json, TextWriter output, TextWriter error)
    {
        string sha256;
        PluginPackage? package = null;
        string? refused = null;

        try
        {
            using (var stream = file.OpenRead()) sha256 = Convert.ToHexStringLower(SHA256.HashData(stream));

            using var read = file.OpenRead();

            try
            {
                package = PluginPackage.ReadAsync(read).GetAwaiter().GetResult();
            }
            catch (InvalidDataException ex)
            {
                refused = ex.Message;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"{GlobalConstants.ApplicationName}: {file.Name}: {ex.Message}");
            return Exit.Failed;
        }

        if (package is { Builds.Count: 0 }) refused = package.Refusal(PluginPackage.ThisPlatform);

        var problem = refused is not null || package!.Builds.Any(b => package.Description(b).Refusal() is not null);

        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(
                new
                {
                    file = file.Name,
                    sha256,
                    size = file.Length,
                    refused,
                    signer = package?.Signer?.Fingerprint,
                    builds = package?.Builds.Select(b => Build(b, package.Description(b))),
                },
                Writing.Json));
        }
        else if (refused is not null)
        {
            output.WriteLine(file.Name);
            output.WriteLine($"  refused   {refused}");
            output.WriteLine($"  sha256    {sha256}");
        }
        else
        {
            Lines(file.Name, package!, output);
        }

        return problem ? Exit.Problems : Exit.Ok;
    }

    /// <summary>What the install dialog shows, as lines.</summary>
    public static void Lines(string name, PluginPackage package, TextWriter writer)
    {
        var plugin = package.Description(package.Builds[0]);

        writer.WriteLine(name);
        writer.WriteLine($"  {plugin.Name} {plugin.Version}{(plugin.Author.Length > 0 ? $", by {plugin.Author}" : "")}");

        if (plugin.Description.Length > 0) writer.WriteLine($"  {plugin.Description.Replace("\n", "\n  ")}");

        writer.WriteLine($"  tags      {(plugin.Tags.Count > 0 ? string.Join(", ", plugin.Tags) : "none")}");
        writer.WriteLine($"  preview   {(plugin.Preview is { } preview ? $"{preview.MediaType}, {Math.Max(1, preview.Bytes.Length >> 10)} KB" : "none")}");
        writer.WriteLine($"  adds      {(plugin.Adds.Count > 0 ? string.Join(", ", plugin.Adds) : "nothing Flyback can find")}");

        if (plugin.Modules.Count > 0)
            writer.WriteLine($"  modules   {string.Join(", ", plugin.Modules.Select(m => $"{m.Name} ({m.TypeId})"))}");
        writer.WriteLine($"  reaches   {(plugin.Reaches.Count > 0 ? string.Join(", ", plugin.Reaches) : "nothing outside Flyback that it names")}");
        writer.WriteLine($"  assembly  {plugin.Assembly}.dll");
        writer.WriteLine($"  against   {plugin.BuiltAgainst}");
        writer.WriteLine($"  builds    {string.Join(", ", package.Builds)}");

        foreach (var build in package.Builds)
        {
            if (package.Description(build).Refusal() is { } refusal) writer.WriteLine($"  refused   {build}: {refusal}");
        }

        writer.WriteLine($"  signed    {(package.Signer is { } signer ? $"key {signer.Fingerprint}" : "no")}");
        writer.WriteLine($"  sha256    {package.Sha256}");
    }

    private static object Build(string system, PluginDescription plugin) => new
    {
        system,
        refusal = plugin.Refusal(),
        assembly = plugin.Assembly,
        name = plugin.Name,
        version = plugin.Version,
        author = plugin.Author,
        description = plugin.Description,
        tags = plugin.Tags,
        preview = plugin.Preview is { } preview ? new { type = preview.MediaType, bytes = preview.Bytes.Length } : null,
        adds = plugin.Adds,
        reaches = plugin.Reaches,
        modules = plugin.Modules.Select(m => new { typeId = m.TypeId, name = m.Name }),
        compiled = plugin.Compiled.Select(c => new
        {
            assembly = c.Assembly,
            references = c.References.Select(r => new { name = r.Name, version = r.Version?.ToString() }),
        }),
    };
}
