namespace Flyback.Server;

/// <summary>
/// The presets the site starts with: patch files shipped beside it in <c>Defaults</c>,
/// added as it starts and kept to the file (ADR-0138).
/// </summary>
internal static class Defaults
{
    /// <remarks>
    /// A file there that is not a patch stops the site starting, as a bad setting does:
    /// it is a broken build, and a shelf quietly short of it would not say so.
    /// </remarks>
    public static void Seed(PresetStore store, string folder, DateTimeOffset at)
    {
        if (!Directory.Exists(folder)) return;

        foreach (var path in Directory.EnumerateFiles(folder).Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(path);

            store.Seed(
                Submissions.Read(name, File.ReadAllBytes(path), name: null)
                    ?? throw new InvalidOperationException($"The default preset {name} is not a patch."),
                at);
        }
    }
}
