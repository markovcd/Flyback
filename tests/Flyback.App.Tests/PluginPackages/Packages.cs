using System.IO.Compression;
using System.Text;
using Flyback.Plugins.Picture;

namespace Flyback.App.Tests.PluginPackages;

/// <summary>Plugin packages built in memory, one zip entry at a time.</summary>
internal static class Packages
{
    /// <summary>A real plugin assembly, built against the contract this Flyback offers.</summary>
    public static byte[] Assembly { get; } = File.ReadAllBytes(typeof(PicturePlugin).Assembly.Location);

    public const string AssemblyName = "Flyback.Plugins.Picture.dll";

    public static string Manifest(
        string id = "acme.ripple",
        string name = "Ripple",
        string version = "1.2.0",
        string? author = "Acme",
        string? description = "Rings on water.",
        string? website = null)
    {
        var fields = new List<string> { $"\"id\": {Quote(id)}", $"\"name\": {Quote(name)}", $"\"version\": {Quote(version)}" };

        if (author is not null) fields.Add($"\"author\": {Quote(author)}");
        if (description is not null) fields.Add($"\"description\": {Quote(description)}");
        if (website is not null) fields.Add($"\"website\": {Quote(website)}");

        return "{" + string.Join(", ", fields) + "}";

        static string Quote(string text) => System.Text.Json.JsonSerializer.Serialize(text);
    }

    /// <summary>A package with a plugin.json and a plugin built for each of <paramref name="platforms"/>.</summary>
    public static byte[] For(params string[] platforms) => Described(Manifest(), platforms);

    public static byte[] Described(string manifest, params string[] platforms)
    {
        var entries = new List<(string, byte[])> { ("plugin.json", Encoding.UTF8.GetBytes(manifest)) };

        foreach (var platform in platforms)
        {
            entries.Add(($"{platform}/{AssemblyName}", Assembly));
            entries.Add(($"{platform}/Flyback.Plugins.Picture.deps.json", Encoding.UTF8.GetBytes("{}")));
            entries.Add(($"{platform}/runtimes/{platform}/native/lib.bin", [1, 2, 3]));
        }

        return Zip(entries);
    }

    public static byte[] Zip(IEnumerable<(string Name, byte[] Bytes)> entries)
    {
        using var memory = new MemoryStream();

        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, bytes) in entries)
            {
                using var stream = zip.CreateEntry(name).Open();
                stream.Write(bytes);
            }
        }

        return memory.ToArray();
    }

    /// <summary>A package with the usual contents and one more entry named <paramref name="name"/>.</summary>
    public static byte[] With(string name, byte[]? bytes = null) =>
        Zip([("plugin.json", Encoding.UTF8.GetBytes(Manifest())), ($"win/{AssemblyName}", Assembly), (name, bytes ?? [0])]);
}
