using System.Security.Cryptography;

namespace Flyback.Plugins.Hosting;

/// <summary>Every file in a plugin folder, by its path inside the folder, with the SHA-256 of its bytes.</summary>
/// <remarks>
/// Every file rather than the plugin's assembly alone: a dependency beside it runs as
/// much as it does. A name starting with a dot is left out, as the host leaves out such folders.
/// </remarks>
internal sealed class PluginFiles
{
    private readonly SortedDictionary<string, string> hashes;

    private PluginFiles(SortedDictionary<string, string> hashes) => this.hashes = hashes;

    /// <summary>Each path, with <c>/</c> between folders, and its hash in lowercase hex.</summary>
    public IReadOnlyDictionary<string, string> Hashes => hashes;

    public static PluginFiles From(IEnumerable<KeyValuePair<string, string>> hashes)
    {
        var sorted = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var (path, hash) in hashes) sorted[path] = hash.ToLowerInvariant();

        return new PluginFiles(sorted);
    }

    public static PluginFiles Of(string folder)
    {
        var sorted = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var root = Path.GetFullPath(folder);

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var path = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');

            if (path.Split('/').Any(part => part.StartsWith('.'))) continue;

            using var stream = File.OpenRead(file);
            sorted[path] = Convert.ToHexStringLower(SHA256.HashData(stream));
        }

        return new PluginFiles(sorted);
    }

    /// <summary>The first file, by path, that is not as it is in <paramref name="expected"/>, or null where none differs.</summary>
    public string? Changed(PluginFiles expected) =>
        hashes.Keys.Union(expected.hashes.Keys)
            .Order(StringComparer.Ordinal)
            .FirstOrDefault(path => hashes.GetValueOrDefault(path) != expected.hashes.GetValueOrDefault(path));
}
