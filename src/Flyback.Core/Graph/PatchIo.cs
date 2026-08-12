using System.Text.Json;
using System.Text.Json.Serialization;

namespace Flyback.Core.Graph;

/// <summary>
/// A patch that was read, and whether the catalogue can actually build it.
/// Separating the two lets the caller decide what an incomplete patch means —
/// the editor refuses it, where a batch renderer might report and carry on.
/// </summary>
public sealed record PatchLoad(
    Patch Patch,
    IReadOnlyList<ModuleProvider> MissingProviders,
    IReadOnlyList<string> UnknownModules)
{
    public bool IsComplete => MissingProviders.Count == 0 && UnknownModules.Count == 0;

    /// <summary>One line naming what is missing. Empty when nothing is.</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>();

            if (MissingProviders.Count > 0)
                parts.Add("needs " + string.Join(", ", MissingProviders.Select(p => $"{p.Name} ({p.Id})")));

            if (UnknownModules.Count > 0)
                parts.Add($"uses {UnknownModules.Count} module{(UnknownModules.Count == 1 ? "" : "s")} this build does not have");

            return parts.Count == 0 ? string.Empty : $"This patch {string.Join(", and ", parts)}.";
        }
    }

    /// <summary>The same thing at length, for somewhere with room for it.</summary>
    public string Detail
    {
        get
        {
            if (IsComplete) return string.Empty;

            var lines = new List<string>();

            if (MissingProviders.Count > 0)
            {
                lines.Add("Plugins this patch needs that are not installed:");
                lines.AddRange(MissingProviders.Select(p => $"    {p.Name}  ({p.Id})"));
            }

            if (UnknownModules.Count > 0)
            {
                if (lines.Count > 0) lines.Add(string.Empty);
                lines.Add("Modules that could not be built:");
                lines.AddRange(UnknownModules.Select(m => $"    {m}"));
            }

            return string.Join(Environment.NewLine, lines);
        }
    }
}

/// <summary>Reads and writes patches as JSON.</summary>
public static class PatchIo
{
    public const string FileExtension = "fbk";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Writes the patch, stamping which plugins it depends on first. The stamp
    /// is put on the patch rather than only into the text, so the object and the
    /// file it was written to agree about what it requires.
    /// </summary>
    public static string ToJson(Patch patch, ModuleCatalog? against = null)
    {
        patch.Requires = RequirementsOf(patch, against ?? NodeCatalog.Current);
        return JsonSerializer.Serialize(patch, Options);
    }

    /// <summary>
    /// Reads a patch and checks it against the catalogue. Both halves of the
    /// check matter: the stamp names plugins that are missing entirely, and the
    /// module ids catch a file that was hand-edited or saved before its plugin
    /// was renamed — a patch can be short of a module without being short of a
    /// plugin it ever recorded.
    /// </summary>
    public static PatchLoad Read(string json, ModuleCatalog? against = null)
    {
        var catalog = against ?? NodeCatalog.Current;
        var patch = JsonSerializer.Deserialize<Patch>(json, Options) ?? new Patch();

        var missing = (patch.Requires ?? [])
            .Where(r => !catalog.HasProvider(r.Id))
            .ToList();

        var unknown = patch.Nodes
            .Select(n => n.TypeId)
            .Distinct(StringComparer.Ordinal)
            .Where(t => catalog.Get(t) is null)
            .Order(StringComparer.Ordinal)
            .ToList();

        return new PatchLoad(patch, missing, unknown);
    }

    public static void Save(Patch patch, string path, ModuleCatalog? against = null) =>
        File.WriteAllText(path, ToJson(patch, against));

    public static PatchLoad Load(string path, ModuleCatalog? against = null) =>
        Read(File.ReadAllText(path), against);

    /// <summary>
    /// Every provider the patch's modules come from, except the engine's own —
    /// listing that would be noise on every file ever saved. Null when there is
    /// nothing to say.
    /// </summary>
    private static List<ModuleProvider>? RequirementsOf(Patch patch, ModuleCatalog catalog)
    {
        var providers = patch.Nodes
            .Select(n => catalog.ProviderOf(n.TypeId))
            .OfType<ModuleProvider>()
            .Where(p => p.Id != NodeCatalog.BuiltInProvider.Id)
            .DistinctBy(p => p.Id)
            .OrderBy(p => p.Id, StringComparer.Ordinal)
            .ToList();

        return providers.Count == 0 ? null : providers;
    }
}
