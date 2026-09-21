using Flyback.App.Controls;
using Flyback.Core.Graph;
using Flyback.Core.Render;

namespace Flyback.App;

/// <summary>
/// The presets somebody saved: the last run of the gallery, and how a patch gets
/// into it.
/// </summary>
/// <remarks>
/// Listed after every preset the program and its plugins offer, so the rows those
/// are on stay put however many are saved. Saving one keeps a copy and nothing
/// else: the patch on the canvas is still whatever document it was.
/// </remarks>
public sealed partial class MainWindow
{
    /// <summary>
    /// The presets somebody saved, or null for a window that keeps none — every
    /// test that did not ask for a folder, which must not see the ones on the
    /// machine running it.
    /// </summary>
    private readonly PresetLibrary? savedPresets;

    /// <summary>What <see cref="presetsPicker"/> lists, which is <see cref="OrderedPresets"/> as of the last save.</summary>
    private List<PatchPreset> offeredPresets = [];

    /// <summary>The patch a preset is, said to be the document on its way to the canvas.</summary>
    /// <remarks>
    /// Built before it is named, so a preset that will not build leaves the title
    /// alone. A saved one carries what it plays, as the bundle it is.
    /// </remarks>
    private Patch Arrive(PatchPreset preset)
    {
        if (savedPresets?.Holding(preset) is { } saved)
        {
            var bundle = saved.Open(plugins.Modules);

            Became(
                preset.Name,
                beside: null,
                bundle.Files.Count > 0 ? new BundleFiles(bundle.Files, soundFolder, pictureFolder) : null);

            return bundle.Patch;
        }

        var built = preset.Build(plugins.Modules);

        Became(preset.Name, beside: null);

        return built;
    }

    /// <summary>What the gallery is told about the saved run, or null where there is none.</summary>
    private YourPresets? Yours() => savedPresets is null
        ? null
        : new YourPresets(
            () => [.. savedPresets.All.Select(entry => entry.Preset)],
            CheckPresetName,
            name => savedPresets?.Named(name) is not null,
            KeepPreset,
            RemovePreset);

    private (bool Allowed, string Hint) CheckPresetName(string name)
    {
        if (savedPresets is null) return (false, "");

        if (name.Trim().Length == 0) return (false, "");

        if (PresetLibrary.Refusal(name) is { } refused) return (false, refused);

        // One name, one row: the Startup patch setting and the title both know a
        // preset by its name alone.
        if (plugins.Presets.Any(preset => string.Equals(preset.Name, name.Trim(), StringComparison.CurrentCultureIgnoreCase)))
            return (false, "A built-in preset is already called that.");

        return savedPresets.Named(name) is { } already
            ? (true, $"Replaces the “{already.Name}” saved already. It will ask first.")
            : (true, "");
    }

    private bool KeepPreset(string name)
    {
        if (savedPresets is null) return false;

        var replacing = savedPresets.Named(name) is not null;

        try
        {
            var kept = savedPresets.Save(name, editor.Patch, Bytes, plugins.Modules);

            RefreshPresetList();

            Report(
                replacing ? $"Replaced the preset “{kept.Name}”." : $"Saved “{kept.Name}” under Your presets.",
                $"Saved as {kept.Path}");

            return true;
        }
        catch (Exception ex)
        {
            Report($"Could not save the preset “{name.Trim()}”: {ex.Message}", savedPresets.Folder);
            return false;
        }
    }

    private void RemovePreset(PatchPreset preset)
    {
        if (savedPresets?.Holding(preset) is not { } saved) return;

        try
        {
            savedPresets.Remove(saved);
            Report($"Deleted the preset “{saved.Name}”.");
        }
        catch (Exception ex)
        {
            Report($"Could not delete the preset “{saved.Name}”: {ex.Message}", saved.Path);
        }

        RefreshPresetList();
    }

    /// <summary>
    /// Lists the presets again after one was saved or deleted, keeping the one on
    /// the canvas selected — or none, where the one on the canvas was deleted.
    /// </summary>
    private void RefreshPresetList()
    {
        var showing = presetShowing >= 0 && presetShowing < offeredPresets.Count ? offeredPresets[presetShowing] : null;

        offeredPresets = OrderedPresets();

        if (presetsPicker is null) return;

        // Set first, so the picker's handler sees the row it is being put on as
        // the one already showing and builds nothing.
        presetShowing = showing is null ? -1 : offeredPresets.IndexOf(showing);

        presetsPicker.ItemsSource = offeredPresets;
        presetsPicker.SelectedIndex = presetShowing;
    }
}
