using System.IO.Compression;
using System.Text;
using Flyback.Plugins.Picture;

namespace Flyback.App.Tests.PluginPackages;

/// <summary>Plugin packages built in memory, one zip entry at a time.</summary>
internal static class Packages
{
    /// <summary>A real plugin assembly, built against the contract this Flyback offers, that adds modules and presets.</summary>
    public static byte[] Assembly { get; } = File.ReadAllBytes(typeof(PicturePlugin).Assembly.Location);

    public const string AssemblyName = "Flyback.Plugins.Picture.dll";

    /// <summary>The folder the picture plugin is installed into.</summary>
    public const string Folder = "Flyback.Plugins.Picture";

    /// <summary>An assembly a plugin might carry beside it, whose code reaches the network.</summary>
    public static byte[] Networking { get; } = File.ReadAllBytes(typeof(System.Net.Http.HttpClient).Assembly.Location);

    /// <summary>A package with the picture plugin built for each of <paramref name="platforms"/>.</summary>
    public static byte[] For(params string[] platforms)
    {
        var entries = new List<(string, byte[])>();

        foreach (var platform in platforms)
        {
            entries.Add(($"{platform}/{AssemblyName}", Assembly));
            entries.Add(($"{platform}/Flyback.Plugins.Picture.deps.json", Encoding.UTF8.GetBytes("{}")));
            entries.Add(($"{platform}/runtimes/{platform}/native/readme.txt", [1, 2, 3]));
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

    /// <summary>A package with the picture plugin for Windows and one more entry named <paramref name="name"/>.</summary>
    public static byte[] With(string name, byte[]? bytes = null) =>
        Zip([($"win/{AssemblyName}", Assembly), (name, bytes ?? [0])]);
}
