namespace Flyback.Core.Graph;

/// <summary>
/// A patch that was read, and whether the catalog can actually build it.
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
    /// not. A newer layout may mean anything, so nothing else read out of the file
    /// is worth reporting alongside it.
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