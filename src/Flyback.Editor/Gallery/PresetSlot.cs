using System.Text;
using Avalonia.Controls;
using Flyback.Editor.Assist;
using Flyback.Editor.Bars;
using Flyback.Editor.Canvas;
using Flyback.Editor.Capture;
using Flyback.Editor.Controls;
using Flyback.Engine.Graph;
using Flyback.Ui.Controls;
using Flyback.Editor.Files;
using Flyback.Editor.Notices;
using Flyback.Editor.PluginPackages;
using Flyback.Editor.Site;
using Flyback.Editor.Statistics;
using Flyback.Core.Graph;
using Flyback.Engine.Render;
using Flyback.Plugins.Hosting;
using Flyback.Ui;

namespace Flyback.Editor.Gallery;

/// <summary>
/// The preset slot at the head of the toolbar, and everything the gallery it opens
/// does: switching the patch to a preset, keeping and deleting saved ones, and
/// opening one shared on the preset site (ADR-0148).
/// </summary>
/// <remarks>
/// Saved presets are listed after every preset the program and its plugins offer,
/// so the rows those are on stay put however many are saved. Saving one keeps a copy
/// and nothing else: the patch on the canvas is still whatever document it was.
/// </remarks>
internal sealed class PresetSlot : IReactTo<DocumentSaved>, IReactTo<TakeMarked>, IReactTo<PatchShowing>, IReactTo<HistoryChanged>
{
    private readonly IDialog dialog;
    private readonly NodeEditor editor;
    private readonly Document document;
    private readonly PatchFiles files;
    private readonly PluginCatalog plugins;
    private readonly ReportLine report;
    private readonly Usage usage;
    private readonly PresetGallery gallery;
    private readonly PresetAudition audition;
    private readonly PresetLibrary saved;
    private readonly SiteAccess site;
    private readonly AssistantConversation conversation;
    private readonly UnsavedWork unsaved;
    private readonly Playback playback;
    private readonly PluginInstalls installs;
    private readonly RecordingState recording;
    private readonly AssistantPanel assistant;
    private readonly Reactions reactions;

    /// <summary>
    /// Not shown, and never opened: what this holds is which preset is on the
    /// canvas, and its selection changing is how a pick from the gallery reaches
    /// the patch. A Picker rather than a plain list because it was the dropdown
    /// before the gallery, and its refusal to move on a keystroke is still what
    /// keeps an arrow at it from discarding the patch.
    /// </summary>
    private readonly Picker picker;
    private readonly Button button;

    /// <summary>What <see cref="picker"/> lists, which is <see cref="Ordered"/> as of the last save.</summary>
    private List<PatchPreset> offered;

    /// <summary>
    /// Which row of <see cref="picker"/> is on the canvas, or -1 for a document
    /// that did not come from that list. What a refused change puts the box back
    /// to, and what a later pick is compared against so re-choosing the same preset
    /// is a no-op rather than a rebuild.
    /// </summary>
    private int showing;

    /// <summary>Set while the picker is put back, whose own handler would otherwise rebuild the patch.</summary>
    private bool restoring;

    /// <param name="saved">
    /// The presets somebody saved, or null for a window that keeps none — every test
    /// that did not ask for a folder, which must not see the ones on the machine
    /// running it.
    /// </param>
    /// <param name="site">Where the gallery asks for shared presets.</param>
    /// <param name="unsaved">The question every route out of a patch asks first.</param>
    /// <param name="playback">Puts a patch that has just arrived on the canvas, from its beginning.</param>
    /// <param name="installs">Offers the plugins a shared preset that could not be opened is short of.</param>
    /// <param name="assistant">Writes out a prompt typed in the gallery, and is sent it once the patch is empty.</param>
    /// <param name="reactions">Asks for the assistant's column, which a prompt sent from the gallery needs open.</param>
    public PresetSlot(
        NodeEditor editor,
        Document document,
        PluginCatalog plugins,
        ReportLine report,
        Usage usage,
        AssistantConversation conversation,
        PatchFiles files,
        PresetGallery gallery,
        PresetAudition audition,
        PresetLibrary saved,
        SiteAccess site,
        UnsavedWork unsaved,
        Playback playback,
        PluginInstalls installs,
        RecordingState recording,
        AssistantPanel assistant,
        Reactions reactions,
        IDialog dialog)
    {
        this.assistant = assistant;
        this.reactions = reactions;
        this.dialog = dialog;
        this.editor = editor;
        this.document = document;
        this.files = files;
        this.plugins = plugins;
        this.report = report;
        this.usage = usage;
        this.gallery = gallery;
        this.audition = audition;
        this.saved = saved;
        this.site = site;
        this.conversation = conversation;
        this.unsaved = unsaved;
        this.playback = playback;
        this.installs = installs;
        this.recording = recording;

        offered = Ordered();

        picker = new Picker
        {
            Name = "presets",
            ItemsSource = offered,
            SelectedIndex = offered.FindIndex(preset => preset.Kind != PresetKind.Blank),
            Width = 34,
            Height = 30,
            Opacity = 0,
            IsHitTestVisible = false,
            IsTabStop = false,
        };

        picker.SelectionChanged += async (_, _) => await PickedAsync();

        // The same square, glyph-only shape as open, save and tidy. It opens the
        // gallery, and a tile picked there is a row of the picker chosen, so there
        // is one road to changing the preset and it is the one that asks about
        // unsaved work.
        button = ToolbarButtons.Drawn("presets-glyph", Glyphs.Presets(), "Start from a preset patch, or save this one as a preset…");
        button.Click += async (_, _) => await ShowGalleryAsync();

        // Stacked in one cell so the toolbar keeps the one slot it had.
        View = new Grid { Children = { picker, button } };
    }

    /// <summary>The slot on the toolbar.</summary>
    public Control View { get; }

    /// <summary>Whatever preset the list still showed, the patch just took on a file of its own.</summary>
    public Task On(DocumentSaved notice)
    {
        Clear();
        return Task.CompletedTask;
    }

    /// <summary>Whatever preset the list still showed is not the patch that arrived.</summary>
    public Task On(PatchShowing notice)
    {
        Clear();
        return Task.CompletedTask;
    }

    /// <summary>An edited preset is a patch of its own, and no longer the preset it started as.</summary>
    public Task On(HistoryChanged notice)
    {
        if (showing >= 0 && editor.History.IsModified) Clear();

        return Task.CompletedTask;
    }

    /// <summary>A take is fed from the patch, so no other patch is offered under one.</summary>
    public Task On(TakeMarked notice)
    {
        SetEnabled(!recording.Running);
        return Task.CompletedTask;
    }

    /// <summary>Enables or disables both ways to open the preset gallery.</summary>
    public void SetEnabled(bool enabled)
    {
        button.IsEnabled = enabled;
        picker.IsEnabled = enabled;
    }

    /// <summary>The preset on the canvas, or null for a document that did not come from the list.</summary>
    public PatchPreset? Showing => offered.ElementAtOrDefault(showing);

    /// <summary>Every preset the gallery and the "Startup patch" list offer, in <see cref="PresetLibrary.Ordered"/>'s order.</summary>
    public List<PatchPreset> Ordered() => PresetLibrary.Ordered(plugins.Presets, saved);

    /// <summary>
    /// Picks the startup patch from the same gallery, with nothing in it to save or
    /// delete: what is chosen is a name, and Cancel drops it.
    /// </summary>
    public async Task<string?> PickStartupPatchAsync(string current)
    {
        var named = Ordered().FirstOrDefault(preset => preset.Name == current);

        var gallery = this.gallery.Build(
            [.. plugins.Presets],
            named,
            pointedAt: audition.PointedAt,
            yours: Yours()?.ToPickFrom(),
            use: "Open on this one");

        var chosen = await dialog.Show("Startup patch", gallery.Tiles, fill: true, wide: true);

        audition.PointedAt(null);

        return chosen?.Name;
    }

    /// <summary>
    /// Takes the selection off the list, for a document that arrived by some other
    /// route — a patch, a bundle, or a source file.
    /// </summary>
    public void Clear()
    {
        showing = -1;
        picker.SelectedIndex = -1;
    }

    /// <summary>
    /// Opens the window on <paramref name="chosen"/>, or on
    /// <see cref="PresetLibrary.Fallback"/> for a name it no longer offers — so the title and the list agree with the
    /// canvas from the first frame (ADR-0093).
    /// </summary>
    public void StartOn(string chosen)
    {
        var at = PresetLibrary.Opening(offered, chosen);

        Patch opened;

        try
        {
            opened = Arrive(offered[at]);
        }
        catch (Exception ex)
        {
            // Only a saved preset can fail here — one whose file has gone bad, or
            // that needs a plugin taken away since. The window still opens, on
            // the preset it would have opened on had none been chosen.
            report.Say($"Could not open the '{offered[at].Name}' preset: {ex.Message}");

            at = PresetLibrary.Opening(offered, "");
            opened = Arrive(offered[at]);
        }

        // Set before the patch, whose first play says which preset it is, and
        // before the picker's own index, so its handler — which rebuilds the
        // patch on a change — sees the row it is already showing and does
        // nothing: the patch below is already built.
        showing = at;

        editor.History.Open(opened);
        picker.SelectedIndex = at;
    }

    /// <summary>What the gallery is told about the saved run, or null where there is none.</summary>
    public YourPresets? Yours() => !saved.Keeps
        ? null
        : new YourPresets(
            () => [.. saved.All.Select(entry => entry.Preset)],
            CheckName,
            name => saved.Named(name) is not null,
            Keep,
            Remove);

    /// <summary>
    /// Opens the shared preset a restart was carrying, found on the site again by its id,
    /// or as it was kept where the site does not answer.
    /// </summary>
    public async Task OpenSharedAgainAsync(string id)
    {
        if (site.Presets() is not { } at) return;

        SitePreset? shared;

        try
        {
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            shared = await at.FindAsync(id, cancel.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        {
            if (site.Kept.Find(at.Root, id) is { } kept)
            {
                await OpenKeptAsync(kept);
                return;
            }

            report.Say("The preset this was restarted for could not be fetched from the preset site again.");
            return;
        }

        if (shared is null)
        {
            TakenDown(at, id, site.Kept.Find(at.Root, id)?.Name);
            return;
        }

        await OpenSharedAsync(shared);
    }

    /// <summary>Forgets a preset the site answered it no longer has, and says so.</summary>
    private void TakenDown(PresetSite at, string id, string? name)
    {
        site.Kept.Forget(at.Root, id);
        report.Say(name is null ? "The preset this was restarted for has been taken off the preset site." : $"“{name}” has been taken off the preset site.");
    }

    private async Task ShowGalleryAsync()
    {
        if (RefuseWhileRecording()) return;

        usage.Count(Used.Gallery);

        var current = picker.SelectedItem as PatchPreset;
        var parts = gallery.Build(
            [.. plugins.Presets],
            current,
            pointedAt: audition.PointedAt,
            yours: Yours(),
            site: site.Presets(),
            kept: site.Kept,
            prompting: assistant.Ready);
        var chosen = await dialog.Show("Start from a preset", parts.Tiles, fill: true, wide: true);

        audition.PointedAt(null);

        if (RefuseWhileRecording()) return;

        switch (chosen)
        {
            // Looked up in the list as it is now, which a save in the gallery may have changed.
            case PatchPreset preset:
                picker.SelectedIndex = offered.IndexOf(preset);
                break;

            case SitePreset shared:
                await OpenSharedAsync(shared);
                break;

            case KeptPreset kept:
                if (await unsaved.MayReplaceThePatchAsync()) await OpenKeptAsync(kept);
                break;

            case PromptedStart prompted:
                await StartFromPromptAsync(prompted);
                break;
        }
    }

    /// <summary>
    /// Puts an empty patch on the canvas, opens the assistant's column and sends it
    /// <paramref name="prompted"/>, written out first, and nothing at all unless the empty patch is put there.
    /// </summary>
    private async Task StartFromPromptAsync(PromptedStart prompted)
    {
        var blank = offered.Find(preset => preset.Name == PresetLibrary.Empty);

        if (blank is null || !await SwitchToAsync(blank, offered.IndexOf(blank))) return;

        await reactions.RaiseAsync(new AssistantAsked(true));

        await assistant.StartFromIdeaAsync(prompted.Prompt);
    }

    private async Task PickedAsync()
    {
        if (restoring) return;

        // Nothing on no selection, and nothing on a heading either — which
        // no pointer can land on, and so can only have been set from here.
        if (picker.SelectedItem is not PatchPreset preset) return;
        if (picker.SelectedIndex == showing) return;

        await SwitchToAsync(preset, picker.SelectedIndex);
    }

    /// <summary>
    /// Puts <paramref name="preset"/>, the row <paramref name="wanted"/> of the picker, on
    /// the canvas after the questions every route out of a patch asks. Answers whether it
    /// is there.
    /// </summary>
    private async Task<bool> SwitchToAsync(PatchPreset preset, int wanted)
    {
        if (RefuseWhileRecording())
        {
            PutTheBoxBack();
            return false;
        }

        if (!await unsaved.MayReplaceThePatchAsync())
        {
            PutTheBoxBack();
            return false;
        }

        if (RefuseWhileRecording())
        {
            PutTheBoxBack();
            return false;
        }

        try
        {
            // A preset from a plugin is built here, not when it was registered,
            // so this is where a plugin that offered a patch using modules it
            // failed to add finally shows up.
            playback.Show(Arrive(preset));

            // A preset has no file to have saved a conversation with, so it
            // arrives with none — ADR-0072.
            conversation.Open(null);

            // A preset arrives as a graph and no text describes it, so the
            // canvas owns it — ADR-0068.
            document.DropSource();

            // Unless it was picked from the text view, where it is read into
            // text there and then: which view somebody picks a preset from
            // says which of the two they mean to work in.
            if (document.ShowingCode) document.ReadIntoText();

            showing = wanted;

            usage.Count(Used.Preset);

            // The question above may have been answered with a save, and a
            // save takes the selection off this list: what was saved is a
            // file, and no preset. The row that was picked is picked again,
            // or the title would name a preset the list does not show.
            PutTheBoxBack();

            return true;
        }
        catch (Exception ex)
        {
            report.Say($"Could not build the '{preset.Name}' preset: {ex.Message}");
            PutTheBoxBack();

            return false;
        }
    }

    private void PutTheBoxBack()
    {
        restoring = true;
        picker.SelectedIndex = showing;
        restoring = false;
    }

    private bool RefuseWhileRecording()
    {
        if (!recording.Running) return false;

        report.Say("Stop the recording before opening a preset.");
        return true;
    }

    /// <summary>The patch a preset is, said to be the document on its way to the canvas.</summary>
    /// <remarks>
    /// Built before it is named, so a preset that will not build leaves the title
    /// alone. Named at all because a preset is one of the ways a patch arrives and
    /// the only one with no file to be named after; it has no folder either. A saved
    /// one carries what it plays, as the bundle it is, and a built one what its
    /// assembly holds.
    /// </remarks>
    private Patch Arrive(PatchPreset preset)
    {
        if (saved.Holding(preset) is { } kept)
        {
            var bundle = kept.Open(plugins.Modules);

            files.Became(
                preset.Name,
                beside: null,
                bundle.Files.Count > 0 ? new BundleFiles(bundle.Files, files.SoundFolder, files.PictureFolder) : null);

            return bundle.Patch;
        }

        var built = preset.Build(plugins.Modules);

        files.Became(
            preset.Name,
            beside: null,
            preset.Files is { } carried ? new BundleFiles(carried(), files.SoundFolder, files.PictureFolder) : null);

        return built;
    }

    private (bool Allowed, string Hint) CheckName(string name)
    {
        if (!saved.Keeps) return (false, "");

        if (name.Trim().Length == 0) return (false, "");

        if (PresetLibrary.Refusal(name) is { } refused) return (false, refused);

        // One name, one row: the Startup patch setting and the title both know a
        // preset by its name alone.
        if (plugins.Presets.Any(preset => string.Equals(preset.Name, name.Trim(), StringComparison.CurrentCultureIgnoreCase)))
            return (false, "A built-in preset is already called that.");

        return saved.Named(name) is { } already
            ? (true, $"Replaces the “{already.Name}” saved already. It will ask first.")
            : (true, "");
    }

    private bool Keep(string name)
    {
        if (!saved.Keeps) return false;

        var replacing = saved.Named(name) is not null;

        try
        {
            var kept = saved.Save(name, editor.History.Patch, files.Bytes, plugins.Modules);

            Refresh();
            usage.Count(Used.KeptPreset);

            report.Say(
                replacing ? $"Replaced the preset “{kept.Name}”." : $"Saved “{kept.Name}” under Your presets.",
                $"Saved as {kept.Path}");

            return true;
        }
        catch (Exception ex)
        {
            report.Say($"Could not save the preset “{name.Trim()}”: {ex.Message}", saved.Folder);
            return false;
        }
    }

    private void Remove(PatchPreset preset)
    {
        if (saved.Holding(preset) is not { } kept) return;

        try
        {
            saved.Remove(kept);
            report.Say($"Deleted the preset “{kept.Name}”.");
        }
        catch (Exception ex)
        {
            report.Say($"Could not delete the preset “{kept.Name}”: {ex.Message}", kept.Path);
        }

        Refresh();
    }

    /// <summary>
    /// Lists the presets again after one was saved or deleted, keeping the one on
    /// the canvas selected — or none, where the one on the canvas was deleted.
    /// </summary>
    private void Refresh()
    {
        var current = Showing;

        offered = Ordered();

        // Set first, so the picker's handler sees the row it is being put on as
        // the one already showing and builds nothing.
        showing = current is null ? -1 : offered.IndexOf(current);

        picker.ItemsSource = offered;
        picker.SelectedIndex = showing;
    }

    /// <summary>
    /// Opens a shared preset as a document named after it, with no folder of its own, as a
    /// preset is: the kept copy where there is one, a shared file never changing, and
    /// otherwise downloaded and kept. Asks about unsaved work first.
    /// </summary>
    private async Task OpenSharedAsync(SitePreset shared)
    {
        if (RefuseWhileRecording()) return;

        if (site.Presets() is not { } at || !await unsaved.MayReplaceThePatchAsync()) return;

        if (await Task.Run(() => KeptFile(at, shared)) is { } same)
        {
            await OpenSharedAsync(shared.Name, shared.FileName, same, new Reopen(Shared: shared.Id), $"Opened “{shared.Name}” from the preset site, as kept on this machine.");
            return;
        }

        report.Say($"Downloading “{shared.Name}” from the preset site…");

        byte[] bytes;
        var still = StillAsync(at, shared);

        try
        {
            bytes = await at.DownloadAsync(shared, CancellationToken.None);
        }
        catch (HttpRequestException ex) when (PresetSite.Gone(ex.StatusCode))
        {
            TakenDown(at, shared.Id, shared.Name);
            return;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            report.Say($"Could not download “{shared.Name}”: {ex.Message}", at.Root.ToString());
            return;
        }

        var stillBytes = await still;

        await Task.Run(() => site.Kept.Keep(at.Root, shared, bytes, stillBytes));

        await OpenSharedAsync(shared.Name, shared.FileName, bytes, new Reopen(Shared: shared.Id));
    }

    /// <summary>The kept copy's file, or null where there is none. The kept copy takes what the site says of it now.</summary>
    private byte[]? KeptFile(PresetSite at, SitePreset shared)
    {
        if (site.Kept.Find(at.Root, shared.Id) is not { } kept || site.Kept.File(kept) is not { } bytes) return null;

        site.Kept.Refresh(at.Root, shared);

        return bytes;
    }

    private static async Task<byte[]?> StillAsync(PresetSite at, SitePreset shared)
    {
        try
        {
            return await at.StillAsync(shared, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>Opens a shared preset as it was kept when it was last opened from the site.</summary>
    private async Task OpenKeptAsync(KeptPreset kept)
    {
        if (await Task.Run(() => site.Kept.File(kept)) is not { } bytes)
        {
            report.Say($"Could not open “{kept.Name}”: what was kept of it cannot be read.");
            return;
        }

        await OpenSharedAsync(kept.Name, kept.Preset.FileName, bytes, new Reopen(Shared: kept.Preset.Id), $"Opened “{kept.Name}” as it was kept, the preset site not answering.");
    }

    /// <summary>
    /// Opens a shared preset's file already fetched from the preset site, as a page that
    /// was sent one does, asking first where there is unsaved work. Answers whether it opened.
    /// </summary>
    public async Task<bool> OpenSharedAsync(string name, string fileName, byte[] bytes)
    {
        if (RefuseWhileRecording() || !await unsaved.MayReplaceThePatchAsync()) return false;

        return await OpenSharedAsync(name, fileName, bytes, new Reopen());
    }

    /// <param name="opened">What the status bar says once it is open, where it is not the usual.</param>
    private async Task<bool> OpenSharedAsync(string name, string fileName, byte[] bytes, Reopen reopen, string? opened = null)
    {
        if (RefuseWhileRecording()) return false;

        try
        {
            Patch patch;
            string? conversation = null;

            if (PatchFileKinds.Bundled(fileName))
            {
                var bundle = PatchBundle.Read(new MemoryStream(bytes, writable: false), plugins.Modules);

                if (bundle.Load is { IsComplete: false } lacking)
                {
                    report.Say($"Not opened. {lacking.Summary}", lacking.Detail);
                    await installs.OfferMissingAsync(lacking, reopen);
                    return false;
                }

                files.Became(name, beside: null, new BundleFiles(bundle.Files, files.SoundFolder, files.PictureFolder));
                patch = bundle.Patch;
                conversation = bundle.Conversation;
            }
            else
            {
                var loaded = PatchIO.Read(Encoding.UTF8.GetString(bytes));

                if (!loaded.IsComplete)
                {
                    report.Say($"Not opened. {loaded.Summary}", loaded.Detail);
                    await installs.OfferMissingAsync(loaded, reopen);
                    return false;
                }

                files.Became(name, beside: null);
                patch = loaded.Patch;
            }

            usage.Count(Used.Opened);
            usage.Count(Used.Shared);

            playback.Show(patch);
            document.DropSource();
            this.conversation.Open(conversation);

            // Read into text where it was picked from the text view, as a preset is.
            if (document.ShowingCode) document.ReadIntoText();

            report.Say(opened ?? $"Opened “{name}” from the preset site.");
            return true;
        }
        catch (Exception ex)
        {
            report.Say($"Could not open “{name}”: {ex.Message}");
            return false;
        }
    }
}
