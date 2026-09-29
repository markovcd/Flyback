using Flyback.Core.Graph;

namespace Flyback.App.Gallery;

/// <summary>
/// The presets somebody saved, and what the gallery may do about them: save the
/// patch on the canvas as another, and delete one.
/// </summary>
/// <param name="All">What is saved now, in the order to show it. Asked again after every change.</param>
/// <param name="Check">
/// Whether a name may be saved under, and a line saying so — why not, or that it
/// replaces one already saved.
/// </param>
/// <param name="Replaces">Whether saving under a name would replace a preset already saved, which is asked about first.</param>
/// <param name="Keep">Saves the patch on the canvas under a name <paramref name="Check"/> allowed. False where that failed.</param>
/// <param name="Remove">Deletes one.</param>
internal sealed record YourPresets(
    Func<IReadOnlyList<PatchPreset>> All,
    Func<string, (bool Allowed, string Hint)> Check,
    Func<string, bool> Replaces,
    Func<string, bool> Keep,
    Action<PatchPreset> Remove)
{
    /// <summary>Whether the run is only picked from: no card to save one, and no tile that deletes.</summary>
    public bool PickOnly { get; private init; }

    /// <summary>The same presets, to pick from and nothing else.</summary>
    public YourPresets ToPickFrom() => this with { PickOnly = true };
}