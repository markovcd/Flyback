using Avalonia.Controls;
using Flyback.App.Controls;
using Flyback.App.Statistics;
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

    /// <summary>Trying a preset from the gallery by resting the pointer on its tile.</summary>
    private readonly PresetAudition audition;

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

            files.Became(
                preset.Name,
                beside: null,
                bundle.Files.Count > 0 ? new BundleFiles(bundle.Files, files.SoundFolder, files.PictureFolder) : null);

            return bundle.Patch;
        }

        var built = preset.Build(plugins.Modules);

        files.Became(preset.Name, beside: null);

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
            var kept = savedPresets.Save(name, editor.Patch, files.Bytes, plugins.Modules);

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

    /// <summary>
    /// The preset slot at the head of the toolbar: a button that opens the gallery,
    /// over the hidden list whose selection is which preset is on the canvas.
    /// </summary>
    private Control BuildPresetSlot()
    {
        offeredPresets = OrderedPresets();

        // Not shown, and never opened: what this holds is which preset is on the
        // canvas, and its selection changing is how a pick from the gallery
        // reaches the code below. A Picker rather than a plain list because it
        // was the dropdown before the gallery, and its refusal to move on a
        // keystroke is still what keeps an arrow at it from discarding the patch.
        var presets = new Picker
        {
            Name = "presets",
            ItemsSource = offeredPresets,
            SelectedIndex = offeredPresets.FindIndex(preset => preset.Kind != PresetKind.Blank),
            Width = 34,
            Height = 30,
            Opacity = 0,
            IsHitTestVisible = false,
            IsTabStop = false,
        };

        presetsPicker = presets;

        // The toolbar button: the same square, glyph-only shape as open, save and
        // tidy. It opens the gallery, and a tile picked there is a row of the
        // picker above chosen, so there is one road to changing the preset and it
        // is the one that asks about unsaved work.
        var presetsButton = ToolbarButtons.Drawn("presets-glyph", Glyphs.Presets(), "Start from a preset patch, or save this one as a preset…");
        presetsButton.Click += async (_, _) =>
        {
            var showing = presets.SelectedItem as PatchPreset;
            var gallery = PresetGallery.Build([.. plugins.Presets.OrderBy(p => p.Kind)], showing, thumbnails, audition.PointedAt, Yours(), PresetSite());
            var chosen = await this.ShowDialog<object?>("Start from a preset", gallery.Tiles, gallery.Filter, fill: true);

            audition.PointedAt(null);

            switch (chosen)
            {
                // Looked up in the list as it is now, which a save in the gallery may have changed.
                case PatchPreset preset:
                    presets.SelectedIndex = offeredPresets.IndexOf(preset);
                    break;

                case SitePreset shared:
                    await OpenSharedPresetAsync(shared);
                    break;
            }
        };

        // Stacked in one cell so the toolbar keeps the one slot it had.
        var presetsSlot = new Grid();
        presetsSlot.Children.Add(presets);
        presetsSlot.Children.Add(presetsButton);

        // Which preset is on the canvas, so a refused change can put the box
        // back where it was. Setting the index raises this same handler, hence
        // the flag around it.
        var restoring = false;

        presets.SelectionChanged += async (_, _) =>
        {
            if (restoring) return;

            // Nothing on no selection, and nothing on a heading either — which
            // no pointer can land on, and so can only have been set from here.
            if (presets.SelectedItem is not PatchPreset preset) return;
            if (presets.SelectedIndex == presetShowing) return;

            var wanted = presets.SelectedIndex;

            if (!await MayReplaceThePatchAsync())
            {
                PutTheBoxBack();
                return;
            }

            try
            {
                // A preset from a plugin is built here, not when it was
                // registered, so this is where a plugin that offered a patch
                // using modules it failed to add finally shows up.
                //
                // Named before it is shown, because showing it is what redraws the
                // title — and named at all because a preset is one of the three ways a
                // patch arrives and the only one with no file to be named after. It has
                // no folder either, and disowns whatever the last document was carrying:
                // a preset naming a sound means the one beside the program, or the one
                // in its own bundle, not the one inside a bundle somebody happened to
                // open first.
                editor.Patch = Arrive(preset);
                RewindToZero();

                // A preset has no file to have saved a conversation with, so it
                // arrives with none — ADR-0072.
                assistant?.Open(null);

                // A preset arrives as a graph and no text describes it, so the
                // canvas owns it — ADR-0068.
                document.DropSource();

                // Unless it was picked from the text view, where it is read into
                // text there and then: which view somebody picks a preset from
                // says which of the two they mean to work in.
                if (document.ShowingCode) document.ReadIntoText();

                presetShowing = wanted;

                usage.Count(Used.Preset);

                // The question above may have been answered with a save, and a
                // save takes the selection off this list: what was saved is a
                // file, and no preset. The row that was picked is picked again,
                // or the title would name a preset the list does not show.
                PutTheBoxBack();
            }
            catch (Exception ex)
            {
                Report($"Could not build the '{preset.Name}' preset: {ex.Message}");
                PutTheBoxBack();
            }
        };

        return presetsSlot;

        void PutTheBoxBack()
        {
            restoring = true;
            presets.SelectedIndex = presetShowing;
            restoring = false;
        }
    }
}
