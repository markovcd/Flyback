using System.Reflection;

namespace Flyback.Core.Graph;

/// <summary>The files a built preset carries, baked into the assembly that ships it.</summary>
public static class PresetFiles
{
    /// <summary>
    /// Every resource of <paramref name="assembly"/> under <paramref name="folder"/>,
    /// keyed by its name within the folder, which is the path the preset's patch
    /// names it by.
    /// </summary>
    /// <remarks>
    /// Embed each file with <c>LogicalName="folder/name.wav"</c>. Read on every
    /// call, since a preset is opened rarely and the bytes are the patch's to keep.
    /// </remarks>
    public static Func<IReadOnlyDictionary<string, byte[]>> Embedded(Assembly assembly, string folder)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        var prefix = folder.TrimEnd('/') + "/";

        return () =>
        {
            var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

            foreach (var name in assembly.GetManifestResourceNames())
            {
                if (!name.StartsWith(prefix, StringComparison.Ordinal) || name.Length == prefix.Length) continue;

                using var stream = assembly.GetManifestResourceStream(name)!;
                using var copy = new MemoryStream();

                stream.CopyTo(copy);
                files[name[prefix.Length..]] = copy.ToArray();
            }

            return files;
        };
    }
}
