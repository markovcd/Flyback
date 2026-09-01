using System.Text.Json;
using System.Text.Json.Serialization;

namespace Flyback.Core.Graph;

/// <summary>
/// A patch that was read, and whether the catalogue can actually build it.
/// Separating the two lets the caller decide what an incomplete patch means —
/// the editor refuses it, where a batch renderer might report and carry on.
/// </summary>
/// <param name="Version">
/// The layout the file declared, or <see cref="PatchIO.FirstVersion"/> where it
/// declared none.
/// </param>
public sealed record PatchLoad(
    Patch Patch,
    IReadOnlyList<ModuleProvider> MissingProviders,
    IReadOnlyList<string> UnknownModules,
    int Version = PatchIO.FirstVersion)
{
    /// <summary>
    /// Whether the file was written by a build that knows a layout this one does
    /// not. The one problem here that cannot be described any further: a newer
    /// layout may mean anything, so nothing else read out of the file — not its
    /// modules, not its plugins — is worth reporting alongside it.
    /// </summary>
    public bool TooNew => Version > PatchIO.FormatVersion;

    public bool IsComplete => !TooNew && MissingProviders.Count == 0 && UnknownModules.Count == 0;

    /// <summary>One line naming what is missing. Empty when nothing is.</summary>
    public string Summary
    {
        get
        {
            if (TooNew)
            {
                return $"This patch was saved by a newer version of {GlobalConstants.ApplicationName} "
                    + $"(file layout {Version}, this build reads {PatchIO.FormatVersion}).";
            }

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

            if (TooNew)
            {
                return "Nothing here can be trusted to mean what it says, so none of it was read. "
                    + $"Update {GlobalConstants.ApplicationName} and open it again, or save it from the build that wrote it "
                    + "in a layout this one knows.";
            }

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
public static class PatchIO
{
    public const string FileExtension = "fbk";

    /// <summary>
    /// The layout this build writes, and the highest it can read.
    /// </summary>
    /// <remarks>
    /// Raised only when the <em>shape</em> of the file changes in a way an older
    /// reader would get wrong: a field renamed, a number that starts counting
    /// from somewhere else, a list that starts meaning something new. Adding a
    /// module is not such a change and must not raise it — a file naming a module
    /// this build has never heard of is already answered, by name and in detail,
    /// through <see cref="PatchLoad.UnknownModules"/>. Raising it for that would
    /// refuse whole patches over one block they might not even miss.
    /// <para>
    /// Every raise owes a step in <c>Upgrade</c>, because a file that was legal
    /// once stays legal: the version says which reading is right, not whether the
    /// file is still welcome.
    /// </para>
    /// </remarks>
    public const int FormatVersion = 1;

    /// <summary>
    /// What a file with no stamp on it is: every patch written before there was a
    /// stamp to write. Deliberately a named constant rather than a bare 1 —
    /// <see cref="FormatVersion"/> will move and this one never can.
    /// </summary>
    public const int FirstVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Writes the patch, stamping the layout and which plugins it depends on
    /// first. Both stamps are put on the patch rather than only into the text, so
    /// the object and the file it was written to agree about what it is and what
    /// it requires.
    /// </summary>
    public static string ToJson(Patch patch, ModuleCatalog? against = null)
    {
        patch.Version = FormatVersion;
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
        var version = VersionOf(json);

        // Read out of the raw text and answered before anything else, because a
        // layout this build does not know is the one problem that makes the rest
        // of the file unreadable rather than merely incomplete. Deserialising
        // first would be guessing at a shape nobody has described yet, and would
        // throw on the shapes it guessed wrong — an exception where there is a
        // perfectly good sentence to say instead.
        if (version > FormatVersion)
        {
            var empty = new Patch();
            empty.EnsureOutput(catalog);

            return new PatchLoad(empty, [], [], version);
        }

        var patch = Upgrade(
            JsonSerializer.Deserialize<Patch>(json, Options) ?? new Patch(),
            version);

        var missing = (patch.Requires ?? [])
            .Where(r => !catalog.HasProvider(r.Id))
            .ToList();

        var unknown = patch.Nodes
            .Select(n => n.TypeId)
            .Distinct(StringComparer.Ordinal)
            .Where(t => catalog.Get(t) is null)
            .Order(StringComparer.Ordinal)
            .ToList();

        // A file is the one way a patch can arrive without one — hand-edited,
        // or written by a build that had a different idea of the sink. Given
        // back rather than refused: a patch short of its Output is a patch with
        // an Output added, and everything else in the file still means what it
        // said.
        if (unknown.Count == 0) patch.EnsureOutput(catalog);

        return new PatchLoad(patch, missing, unknown, version);
    }

    /// <summary>
    /// The layout a file declares, taken from the raw text rather than from a
    /// deserialised patch — the whole point being to learn this about files that
    /// cannot be deserialised into one.
    /// </summary>
    /// <remarks>
    /// Anything unreadable as a version is <see cref="FirstVersion"/>. For a file
    /// with no stamp that is the truth. For one whose stamp is nonsense it is
    /// merely the answer that keeps this quiet: the file is malformed and the
    /// deserialiser below will say so in the ordinary way, which is a better
    /// complaint than either guessing a layout or reporting a file from the
    /// future that nobody wrote.
    /// </remarks>
    private static int VersionOf(string json)
    {
        using var document = JsonDocument.Parse(json);

        // The kind is checked before the value because TryGetInt32 throws rather
        // than answering false when the element is not a number at all — the one
        // case a stamp somebody typed by hand is most likely to be.
        return document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty(nameof(Patch.Version), out var stamp)
            && stamp.ValueKind == JsonValueKind.Number
            && stamp.TryGetInt32(out var version)
                ? version
                : FirstVersion;
    }

    /// <summary>
    /// Brings a patch read from an older layout up to what this build expects.
    /// </summary>
    /// <remarks>
    /// Empty, because version 1 is the only layout there has ever been. It is
    /// called anyway, and written out rather than left to be invented later, so
    /// that the first change to the format has one obvious place to go and cannot
    /// be done by quietly reinterpreting a field instead.
    /// <para>
    /// Steps belong here in order and each must stand alone — a file at version 1
    /// opened by a build at version 4 runs 1→2, 2→3 and 3→4 in turn, so no step
    /// may assume anything but the layout immediately before it.
    /// </para>
    /// </remarks>
    /// <param name="patch"></param>
    /// <param name="from">The layout the file was written in, never above <see cref="FormatVersion"/>.</param>
    private static Patch Upgrade(Patch patch, int from)
    {
        _ = from;
        return patch;
    }

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
