using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Flyback.App.Controls;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Flyback.Plugins;
using Flyback.Plugins.Settings;
using Colors = Flyback.App.Controls.Colors;

namespace Flyback.App;

/// <summary>
/// The panel on the right: the selected module's knobs, and — for the Output,
/// which every patch has — the settings of the instrument itself.
/// </summary>
/// <remarks>
/// Rebuilt from nothing every time the selection changes, because what it shows
/// is entirely the selected module's port list. The Output's own controls are the
/// exception and are wired once from the constructor: they are the state of the
/// instrument rather than of a selection, so they work before anything is
/// selected and keep their values across every rebuild.
/// </remarks>
public sealed partial class MainWindow
{
    /// <summary>The frame rates a take can be recorded at: film, PAL, the usual, and the two doubles.</summary>
    private static readonly double[] FrameRates = [24, 25, 30, 50, 60];

    /// <summary>
    /// The frame rates the preview itself can be capped to, 0 standing for
    /// uncapped — the first row, since <see cref="Nearest"/> reads the list as
    /// ascending and a saved 0 should not land on 24 for being the closest
    /// positive number.
    /// </summary>
    private static readonly double[] PreviewFrameRates = [0, 24, 25, 30, 50, 60];

    /// <summary>The latencies the speakers can be asked for, in milliseconds.</summary>
    private static readonly int[] Latencies = [10, 20, 30, 50, 100, 200];

    /// <summary>
    /// The counts a take can be counted in for, in seconds, 0 standing for none
    /// — the first row, for the reason <see cref="PreviewFrameRates"/> puts its
    /// own 0 first.
    /// </summary>
    private static readonly int[] CountIns = [0, 1, 2, 3, 5, 10];

    private const string GpuTip =
        "Draw the picture with a shader on the GPU, or on the CPU. Switch to the CPU to " +
        "compare the two, or if a long session starts to look stepped.";

    /// <summary>
    /// Sets up the controls in the settings window's Graphics section. Called
    /// once, from the constructor, rather than when that window opens: what was
    /// last saved has to be in force before anybody has looked at them.
    /// </summary>
    /// <remarks>
    /// The controls themselves act on nothing. Picking a size or flicking a
    /// switch is a draft until Save, which is the only thing that hands the
    /// section's values to the preview and the sound — see
    /// <see cref="UseOutputSettings"/>.
    /// </remarks>
    private void WireOutputControls()
    {
        // On by default, because it is the one that keeps up with a large patch.
        // Turning it off is how two backends get compared, and the answer to a
        // long session drifting — see ADR-0035 on float32 and the phase
        // accumulator. It disables itself if the GPU turns out to be unusable.
        gpuButton.SelectedIndex = 0;

        compiler.Failed += message => Dispatcher.UIThread.Post(() => Report(message));

        preview.BackendChanged += message =>
        {
            // The picture's program is only worth compiling while the processor is
            // the one drawing it; the shader has code of its own.
            if (preview.Backend == PreviewBackend.Cpu) compiler.Submit(preview.Program, IlLane.Picture);

            // The choice rather than what is running: a patch the shader cannot
            // draw puts the picture on the processor without anybody having
            // asked, and a box that put itself back to CPU would then be read as
            // the setting having changed — and would be saved as changed the next
            // time anybody pressed Save.
            gpuButton.SelectedIndex = preview.Wanted == PreviewBackend.Gpu ? 0 : 1;
            gpuButton.IsEnabled = preview.GpuAvailable;
            ToolTip.SetTip(gpuButton, preview.GpuAvailable ? GpuTip : message);
            Report(message);
        };

        // The picture a take was reading has gone. Finishing the file is the only
        // useful thing left to do with it — what is already written is a
        // recording, and what would follow is the same frame for ever.
        preview.CaptureLost += Stop;

        // Shown while it is greyed out too, because a disabled control that will
        // not say why is the most annoying thing a panel can contain.
        ToolTip.SetShowOnDisabled(recordButton, true);

        recordButton.Click += async (_, _) => await ToggleRecordAsync();

        BuildGraphicsSection();
        BuildRecordingSection();
        BuildSoundSection();
        BuildMidiSection();

        // Quietly, because nobody asked for anything yet: a saved answer is
        // what the program starts in, not a change to report.
        ShowOutputSettings(outputSettings);
        UseOutputSettings(outputSettings);
    }

    /// <summary>
    /// Puts the Graphics section's controls to <paramref name="settings"/>, and
    /// nothing else — what the preview and the compiler are doing is
    /// <see cref="UseOutputSettings"/>'s business.
    /// </summary>
    private void ShowOutputSettings(OutputSettings settings)
    {
        resolution.SelectedIndex = SizeRow(settings);

        // A box greyed out by a GPU that failed shows what is running, which
        // the BackendChanged handler above already set.
        if (gpuButton.IsEnabled) gpuButton.SelectedIndex = settings.Gpu ? 0 : 1;

        ShowStartupPatch(settings.DefaultPreset);

        frameRate.SelectedIndex = Nearest(FrameRates, settings.FrameRate);
        previewFrameRate.SelectedIndex = Nearest(PreviewFrameRates, settings.PreviewFrameRate);
        jpegQuality.Value = settings.JpegQuality;

        countIn.SelectedIndex = Nearest(CountIns.Select(s => (double)s).ToArray(), settings.CountInSeconds);
        rewindBeforeTake.IsChecked = settings.RewindBeforeTake;

        videoFormat.SelectedIndex = Row(ClipFormats.Pictures, settings.VideoFormat, picture: true);
        soundFormat.SelectedIndex = Row(ClipFormats.Sounds, settings.SoundFormat, picture: false);
        ffmpegBox.Text = settings.FfmpegPath;
        latency.SelectedIndex = Nearest(Latencies.Select(ms => (double)ms).ToArray(), settings.LatencyMilliseconds);
        takeover.SelectedIndex = settings.Takeover == Midi.Takeover.PickUp ? 1 : 0;
        keyboardLayout.SelectedIndex = settings.Keyboard == Midi.KeyboardLayout.Scale ? 1 : 0;

        if (plugins.PreferredAudioOutput is { } output)
            soundForm.Show(output.Form, settings.SoundOf(output.Id));
    }

    /// <summary>
    /// Shows which preset the window starts on, listing the one chosen even where
    /// this launch does not offer it.
    /// </summary>
    /// <remarks>
    /// A plugin's preset, on a launch the plugin is away from. The window opened
    /// on the first patch instead, and showing that row here would have the next
    /// Save — of anything at all — write it over the choice. Listed at the end, as
    /// a choice row lists a device that is switched off, the choice is what Save
    /// writes back and is still there when the plugin is. Nothing chosen yet shows
    /// the row the window opens on, which is what saving it then means.
    /// </remarks>
    private void ShowStartupPatch(string chosen)
    {
        var presets = OrderedPresets();
        var offered = presets.Select(preset => preset.Name).ToList();

        if (chosen.Length > 0 && !offered.Contains(chosen)) offered.Add(chosen);

        defaultPreset.ItemsSource = offered;

        defaultPreset.SelectedIndex = offered.IndexOf(chosen) is >= 0 and var listed
            ? listed
            : PresetLibrary.Opening(presets, chosen);
    }

    /// <summary>
    /// The row of a list nearest a saved value, so a value written by hand that
    /// the list does not offer shows as the closest one that it does.
    /// </summary>
    private static int Nearest(IReadOnlyList<double> rows, double value) =>
        Enumerable.Range(0, rows.Count).MinBy(row => Math.Abs(rows[row] - value));

    /// <summary>
    /// The row of a format list a saved id is. The nearest thing a list of names
    /// has to <see cref="Nearest"/>: an id this build does not define shows as
    /// the format written here, which is the first row of either list.
    /// </summary>
    private static int Row(IReadOnlyList<ClipFormat> formats, string? id, bool picture)
    {
        var wanted = ClipFormats.Wanted(id, picture);

        // Nought either way: it is where the format written here sits in both
        // lists, and so is both the answer and the fallback.
        return Enumerable.Range(0, formats.Count).FirstOrDefault(row => formats[row] == wanted);
    }

    /// <summary>
    /// Hands <paramref name="settings"/> to the preview and the sound. The only way
    /// anything in the Graphics section reaches either.
    /// </summary>
    private void UseOutputSettings(OutputSettings settings)
    {
        var size = Resolutions.All[SizeRow(settings)].Size;

        preview.Resolution = size;
        preview.Use(settings.Gpu ? PreviewBackend.Gpu : PreviewBackend.Cpu);
        preview.FrameRate = settings.PreviewFrameRate;

        // What a live Scan reaches with Coordinates' aspect (ADR-0077) — kept in
        // step with the preview rather than fixed, now that the size list is not
        // all one shape.
        audio.Aspect = SynthRenderer.AspectOf(size.Width, size.Height);

        controls.Takeover = settings.Takeover;
    }

    /// <summary>The row of the size list a saved size is, or the default for one the list no longer offers.</summary>
    private static int SizeRow(OutputSettings settings)
    {
        var row = Array.FindIndex(Resolutions.All,
            r => r.Size.Width == settings.Width && r.Size.Height == settings.Height);

        return row < 0 ? Resolutions.Default : row;
    }

    /// <summary>
    /// Takes what the Graphics section's controls hold as the settings, puts them
    /// in force, and writes them out when there is somewhere to. A failure to
    /// write is said, not thrown: they are in force for this run regardless.
    /// </summary>
    private void SaveOutputSettings()
    {
        // Greyed out for the length of a take, whose file has committed to a size
        // and drops every frame that arrives at another. Greying a box does not
        // take back a row already picked in it — during the count-in, say — so
        // what it holds is not read while it is grey, and the row is put back.
        if (!resolution.IsEnabled) resolution.SelectedIndex = SizeRow(outputSettings);

        var size = Resolutions.All[Math.Max(resolution.SelectedIndex, 0)].Size;
        var before = outputSettings;

        outputSettings = new OutputSettings
        {
            Width = size.Width,
            Height = size.Height,
            // A box greyed out by a GPU that failed says nothing about what
            // was wanted, so the last answer is kept for a launch that has one.
            Gpu = gpuButton.IsEnabled ? gpuButton.SelectedIndex == 0 : outputSettings.Gpu,

            DefaultPreset = defaultPreset.SelectedItem as string ?? outputSettings.DefaultPreset,

            FrameRate = FrameRates[Math.Max(frameRate.SelectedIndex, 0)],
            PreviewFrameRate = PreviewFrameRates[Math.Max(previewFrameRate.SelectedIndex, 0)],

            // An emptied box keeps what was saved rather than becoming nought.
            JpegQuality = jpegQuality.Value is { } quality
                ? Math.Clamp((int)Math.Round(quality), OutputSettings.LowestQuality, OutputSettings.HighestQuality)
                : outputSettings.JpegQuality,

            LatencyMilliseconds = Latencies[Math.Max(latency.SelectedIndex, 0)],

            CountInSeconds = CountIns[Math.Max(countIn.SelectedIndex, 0)],
            RewindBeforeTake = rewindBeforeTake.IsChecked == true,

            VideoFormat = Chosen(ClipFormats.Pictures, videoFormat).Id,
            SoundFormat = Chosen(ClipFormats.Sounds, soundFormat).Id,

            // Trimmed, because a path pasted in with a space on the end is a
            // path nobody meant and one File.Exists would refuse.
            FfmpegPath = (ffmpegBox.Text ?? string.Empty).Trim(),

            // Every backend's, not only the one showing, so a backend that is not
            // installed this launch keeps what it was set to.
            // A copy, because the backend's answers are about to be written into it
            // and what it held before is still to be compared against.
            Sound = new(before.Sound, StringComparer.Ordinal),

            Takeover = takeover.SelectedIndex == 1 ? Midi.Takeover.PickUp : Midi.Takeover.Jump,
            Keyboard = keyboardLayout.SelectedIndex == 1 ? Midi.KeyboardLayout.Scale : Midi.KeyboardLayout.Piano,
        };

        // So an emptied box says what it kept, the next time it is looked at.
        jpegQuality.Value = outputSettings.JpegQuality;

        var soundChanged = false;

        if (plugins.PreferredAudioOutput is { } output)
        {
            outputSettings.RememberSound(output.Id, soundForm.Values);
            soundChanged = !SameAnswers(output.Form(soundForm.Values), before.SoundOf(output.Id), soundForm.Values);
        }

        UseOutputSettings(outputSettings);

        if (outputSettings.LatencyMilliseconds != before.LatencyMilliseconds || soundChanged)
            ReopenAudio();

        if (outputSettingsPath is null) return;

        try
        {
            outputSettings.Save(outputSettingsPath);
        }
        catch (Exception ex)
        {
            Report($"Could not save the output settings: {ex.Message}", outputSettingsPath);
        }
    }

    /// <summary>
    /// Whether two sets of answers mean the same on every field declared, read the
    /// way the backend reads them — so a device picked and then picked back is not a
    /// change, though the bag now holds a key it did not.
    /// </summary>
    private static bool SameAnswers(IReadOnlyList<SettingField> fields, SettingValues a, SettingValues b) =>
        fields.All(field =>
            field.Sane(a.All.GetValueOrDefault(field.Key)) == field.Sane(b.All.GetValueOrDefault(field.Key)));

    /// <summary>
    /// The preview, the splitter under it and the inspector, down one column of
    /// <paramref name="grid"/>, whose three rows are theirs.
    /// </summary>
    private void BuildRightPanel(Grid grid, int column)
    {
        previewBox = new Border
        {
            Background = Brushes.Black,
            Child = preview,
        };

        // Double-click the picture and it takes the window; double-click it or
        // press Escape to put everything back. The gesture every video player
        // already has, on the one control here that is a video.
        previewBox.DoubleTapped += (_, e) =>
        {
            ToggleFullScreenPreview();
            e.Handled = true;
        };

        Grid.SetColumn(previewBox, column);
        Grid.SetRow(previewBox, 0);

        previewRow = grid.RowDefinitions[0];

        var splitter = previewSplitter = new GridSplitter { Background = Brushes.Transparent, Height = 5 };
        Grid.SetColumn(splitter, column);
        Grid.SetRow(splitter, 1);

        // The plate is docked rather than scrolled: what a block is and the buttons
        // that act on it are wanted wherever the reading has been scrolled to.
        var reading = new DockPanel();

        DockPanel.SetDock(plateHost, Dock.Top);

        reading.Children.Add(plateHost);
        reading.Children.Add(new ScrollViewer
        {
            Content = inspector,

            // Explicitly transparent: a theme that gave the scroll viewer a
            // background would paint straight over the wash and the mark.
            Background = Brushes.Transparent,
        });

        // The block's face sits behind the inspector rather than beside it, and
        // never takes a click.
        var inspectorBorder = inspectorBox = new Border
        {
            Background = new SolidColorBrush(Colors.Panel),
            Child = new Panel { Children = { wash, reading } },
        };

        // The mark starts under the plate, whatever height the name and the buttons
        // have left it at.
        // The band and the mark are drawn on the wash, so it is told how deep the
        // name's row is and how far down the plate reaches.
        plateHost.PropertyChanged += (_, e) =>
        {
            if (e.Property != BoundsProperty) return;

            wash.Below = plateHost.Bounds.Height;
            wash.BandHeight = (plateHost.Content as ModulePlate)?.Band ?? 0;
        };
        Grid.SetColumn(inspectorBorder, column);
        Grid.SetRow(inspectorBorder, 2);

        // Over the preview's own cell while it has the window, and nowhere otherwise.
        var overlay = transportOverlay = new TransportOverlay(this) { IsVisible = false };

        overlay.PauseClicked += TogglePause;
        overlay.MuteClicked += ToggleMute;
        overlay.RewindClicked += RewindToZero;

        grid.Children.Add(previewBox);
        grid.Children.Add(overlay);
        grid.Children.Add(splitter);
        grid.Children.Add(inspectorBorder);
    }

    /// <summary>
    /// What the panel's rows are, as against what is in them: which module is being
    /// shown, and which of its inputs have a wire on them.
    /// </summary>
    /// <remarks>
    /// A row is a knob or the word "patched" and never both, so a wire landing on
    /// the selected module changes the panel as much as selecting another does.
    /// Nothing else the canvas reports does, which is what keeps a slider mid-drag
    /// from being torn down under the hand holding it.
    /// </remarks>
    private string inspectorShape = string.Empty;

    /// <summary>
    /// Rebuilds the panel if a wire has arrived at or left the module it is
    /// showing. Hung off every patch change, because patching is not a selection
    /// change and the panel has no other way to hear about it.
    /// </summary>
    private void SyncInspector()
    {
        if (InspectorShape.Of(editor) != inspectorShape) BuildInspector();
    }

    /// <summary>
    /// Rebuilt whenever the selection changes, and whenever a wire changes what
    /// the selected module's rows are. The canvas handles patching; exact
    /// numbers are easier to set with real controls than by dragging on a knob.
    /// </summary>
    private void BuildInspector()
    {
        inspectorShape = InspectorShape.Of(editor);
        inspector.Children.Clear();
        plateHost.Content = null;

        // What an empty panel says depends on which canvas is under it. Naming
        // gestures that are switched off would be worse than saying nothing: a
        // person following them would conclude the program was broken rather
        // than that the patch belongs to the text — see ADR-0068.
        if (editor.SelectedNode is not { } node || NodeCatalog.Get(node.TypeId) is not { } def)
        {
            wash.Clear();
            plateHost.Content = null;

            inspector.Children.Add(new TextBlock
            {
                Text = adrift ? InspectorHelp.Adrifting : editor.Locked ? InspectorHelp.Locked : InspectorHelp.Canvas,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Text.Muted,
                FontSize = Text.Body,
            });
            return;
        }

        // A selection that is exactly a group is about the group, not about
        // whichever of its modules the pointer last came down on. Ahead of
        // everything below, because none of it applies: a box has no knobs, no
        // description and no category — what it has is an edge.
        if (editor.SelectedGroup is { } group)
        {
            BuildGroupInspector(group);
            return;
        }

        // The block's own face, so the panel and the canvas are plainly the same
        // module. Its name and what can be done to it stand on the plate; the rows
        // below stay on the panel, which is what a column of numbers is read on.
        var plate = ModulePlate.Of(def);

        wash.Show(def);

        plate.Named.Children.Add(BuildTitle(node, def, plate.Ink));

        plate.Named.Children.Add(new TextBlock
        {
            Text = def.Category,
            FontSize = Text.Small,
            Foreground = plate.Quiet,
            TextAlignment = TextAlignment.Right,
        });

        plateHost.Content = plate;

        // What can be done to the module goes under its name, above the
        // description — see ActionRow. Where it goes is settled here and what is
        // in it at the end, because a knob does not decide whether a module can
        // be grouped.
        var above = plate.Under.Children.Count;

        if (!string.IsNullOrEmpty(def.Description))
            inspector.Children.Add(new TextBlock
            {
                Text = def.Description,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Text.Muted,
                FontSize = Text.Body,
                Margin = new Thickness(0, 4, 0, 6),
            });

        if (BuildNormalledNote(node, def) is { } normalled) inspector.Children.Add(normalled);

        // Checked once for the whole panel rather than row by row, so that a
        // bar is the same width down the entire module: a module where nothing
        // has a reading gives every slider the column back, and a module where
        // even one socket does reserves it for all of them, named or not.
        var reading = InspectorRows.ShowsReading(def);

        for (var i = 0; i < def.Inputs.Count; i++)
            inspector.Children.Add(BuildInputRow(def, node, def.Inputs[i], i, reading));

        // Whatever the module carries that is not a knob, each kind edited by the
        // control that suits it. This mapping lives here rather than on the extra
        // because it is the one part of a kind that needs Avalonia, which the
        // engine does not reference.
        foreach (var extra in def.Extras)
            if (EditorFor(extra, node, def, reading) is { } control)
                inspector.Children.Add(control);

        if (BuildKeyboardSection(node, def) is { } keyboard) inspector.Children.Add(keyboard);

        if (def.Inputs.Count == 0 && def.Extras.Count == 0)
            inspector.Children.Add(new TextBlock
            {
                Text = "This module has nothing to set — it only produces.",
                Foreground = Text.Muted,
                FontSize = Text.Body,
            });

        // The Output cannot be deleted, so it gets no button for it. What the
        // picture is drawn at and by is a property of the machine rather than of
        // this block, and is in the settings window — ADR-0082.
        if (NodeCatalog.IsSink(node.TypeId))
        {
            Undescribed();
            return;
        }

        // What the graph is made of belongs to whoever owns it. A knob turned on
        // a locked canvas is written back into the text (ADR-0068); a module
        // deleted from one could not be, so the button is not offered rather
        // than offered and undone by the next apply.
        if (editor.Locked)
        {
            Undescribed();
            return;
        }

        var actions = ActionRow();

        // First in the row, because it is the one action here that changes what
        // the patch does rather than how it is drawn. It counts the selection the
        // way delete does, and leaves the Output out of the count for the same
        // reason: that one is never switched off.
        var switching = editor.Switchable;

        if (switching > 0)
        {
            var back = editor.SelectionIsOff;

            Act(
                "switch-modules",
                Glyphs.Switch(),
                (switching > 1, back) switch
                {
                    (true, true) => $"Switch these {switching} modules back on  (Ctrl+B)",
                    (true, false) => $"Switch these {switching} modules off  (Ctrl+B)",
                    (false, true) => "Switch this module back on  (Ctrl+B)",
                    (false, false) =>
                        "Switch this module off, passing what is patched into it straight through  (Ctrl+B)",
                },
                editor.SwitchSelected);
        }

        // Grouping comes ahead of deleting, so the destructive button is at the far
        // end of the row rather than the first thing under the pointer.
        //
        // Its tip counts the way delete's does — see NodeEditor.Groupable — and it
        // is offered on the same terms Ctrl+G is: a button offering to group one
        // module would offer something the graph refuses. Ungrouping is not here,
        // because a selection that is exactly a group gets a panel of its own.
        if (editor.Groupable >= NodeGroup.Fewest)
            Act("group", Glyphs.Group(), $"Draw these {editor.Groupable} modules as one box  (Ctrl+G)", editor.GroupSelected);

        // For a selection that reaches into groups without being one: the group
        // panel above answers only a selection that is exactly one, and a
        // double-click only the box it lands on.
        var shut = editor.SelectedGroups.Count(g => g.Collapsed);
        var open = editor.SelectedGroups.Count(g => !g.Collapsed);

        if (shut > 0)
            Act(
                "open-groups",
                Glyphs.OpenBox(),
                shut > 1
                    ? $"Open the {shut} boxes the selection touches  (Ctrl+E)"
                    : "Open the box, showing the modules in it  (Ctrl+E)",
                editor.OpenSelectedGroups);

        if (open > 0)
            Act(
                "close-groups",
                Glyphs.ShutBox(),
                open > 1
                    ? $"Close the {open} boxes the selection touches  (Ctrl+Shift+E)"
                    : "Close the box, drawing its modules as one  (Ctrl+Shift+E)",
                editor.CloseSelectedGroups);

        // Delete takes the whole selection, the same as the key does, so the tip
        // counts it. Sinks are left out of the count because the graph refuses
        // them: a button offering to delete three when it can only manage two
        // would be lying about what pressing it does.
        var going = editor.SelectedNodes.Count(n => !NodeCatalog.IsSink(n.TypeId));

        Act(
            "delete-modules",
            Glyphs.Delete(),
            going > 1 ? $"Delete these {going} modules  (Delete)" : "Delete this module  (Delete)",
            editor.DeleteSelected);

        plate.Under.Children.Insert(above, actions);

        Undescribed();

        // Last, under everything the module has: it is a note about the
        // assistant, and the one thing on the panel not about the patch.
        void Undescribed()
        {
            if (!editor.Undescribed.Contains(def.TypeId)) return;

            inspector.Children.Add(new TextBlock
            {
                Name = "undescribedNote",
                Text = AssistantPanel.UndescribedNote,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Text.Muted,
                FontSize = Text.Small,
                Margin = new Thickness(0, 18, 0, 0),
            });
        }

        void Act(string name, Control icon, string tip, Action gesture)
        {
            var button = Drawn(name, icon, tip);

            button.Click += (_, _) => gesture();
            actions.Children.Add(button);
        }
    }

    /// <summary>
    /// The strip of buttons under the name at the top of the panel: what can be
    /// done to what is selected, each a glyph with the sentence in its tip.
    /// </summary>
    /// <remarks>
    /// A row rather than a column, because a glyph is the width of a button and a
    /// column of them would leave the panel empty beside it. The tip is the only
    /// place a button without words can say what it does, so every one has one and
    /// it carries the count where there is one to carry.
    /// </remarks>
    private static StackPanel ActionRow() => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 6,
        Margin = new Thickness(0, 2, 0, 6),

        // The right, where the name and the category are: everything the plate
        // carries is read down the one edge.
        HorizontalAlignment = HorizontalAlignment.Right,
    };

    /// <summary>
    /// What a group shows: its name, its edge, and what can be done to it.
    /// </summary>
    /// <remarks>
    /// The edge rather than the contents, which is the whole point of the panel: a
    /// box is a promise that several modules can be thought about as one thing, and
    /// a list of the knobs inside would be the box admitting it never was.
    /// </remarks>
    private void BuildGroupInspector(NodeGroup group)
    {
        // The box's own face, in the grays the canvas draws one in — a box belongs to
        // no category, so there is no accent to carry over.
        var plate = ModulePlate.Box();

        wash.ShowBox();

        plate.Named.Children.Add(BuildGroupTitle(group, plate.Ink));

        plate.Named.Children.Add(new TextBlock
        {
            Text = group.Name is null ? "Group" : $"Group · {group.Counted}",
            FontSize = Text.Small,
            Foreground = plate.Quiet,
            TextAlignment = TextAlignment.Right,
        });

        plateHost.Content = plate;

        // Under the name, above the description, where a module's own row sits.
        var above = plate.Under.Children.Count;

        inspector.Children.Add(new TextBlock
        {
            Text = "Several modules drawn as one. Nothing about the patch changes — the modules "
                 + "are where they were and so are the wires between them.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Text.Muted,
            FontSize = Text.Body,
            Margin = new Thickness(0, 4, 0, 6),
        });

        var sockets = editor.Patch.SocketsOf(group);

        Edge("In", sockets.Inputs);
        Edge("Out", sockets.Outputs);

        if (sockets.Rows == 0)
            inspector.Children.Add(new TextBlock
            {
                Text = "Nothing has been wired across its edge, so the box has no sockets yet.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Text.Muted,
                FontSize = Text.Body,
            });

        // A box on a locked canvas is drawn from the text's group statements, so
        // everything that would change one is left off for the same reason
        // deleting a module is — see BuildInspector.
        if (editor.Locked) return;

        var actions = ActionRow();

        // First in the row, as it is on a module's panel: the one action here that
        // changes what the patch does rather than how it is drawn. It switches the
        // modules, since that is all a group is — a box round some of them.
        var switching = editor.Switchable;

        if (switching > 0)
            Act(
                "switch-group",
                Glyphs.Switch(),
                editor.SelectionIsOff
                    ? $"Switch the {switching} modules in this box back on  (Ctrl+B)"
                    : $"Switch the {switching} modules in this box off, passing what is patched "
                      + "into it straight through  (Ctrl+B)",
                editor.SwitchSelected);

        Act(
            group.Collapsed ? "open-group" : "close-group",
            group.Collapsed ? Glyphs.OpenBox() : Glyphs.ShutBox(),
            group.Collapsed
                ? "Open the box, showing the modules in it"
                : "Close the box, drawing its modules as one",
            editor.ToggleSelectedGroup);

        // Keeping one is not an edit to the patch, so it sits with the one that is
        // not either and ahead of the two that are. What the module list will call
        // it is its name and nothing else — so a group with none is offered the
        // button greyed rather than a button that saves "3 modules" under a heading
        // full of other things called "3 modules". The way out is one gesture up:
        // the title at the top of this panel renames on a double-click.
        var named = !string.IsNullOrWhiteSpace(group.Name);

        // Which of the two things pressing it does, said before it is pressed.
        // Replacing is the one worth knowing about in advance — it is somebody
        // else's group going, and the name is the only warning there is.
        var keep = Act("keep-group", Glyphs.Keep(), !named
            ? "The module list calls a kept group by its name — double-click the title above to give it one."
            : groups?.Named(group.Name) is not null
                ? $"Replaces the “{group.Name}” already in the module list. It will ask first."
                : $"Keeps “{group.Name}” under Groups in the module list, ready to add again.",
            () => KeepGroup(group, actions));

        keep.IsEnabled = named;

        // The greyed one is precisely the one with something to explain, and a
        // tip that will not show on a disabled control explains it to nobody.
        // The two buttons in the toolbar above that grey themselves out do the
        // same for the same reason.
        ToolTip.SetShowOnDisabled(keep, true);

        Act("ungroup", Glyphs.Ungroup(), "Take the box off, leaving the modules where they are  (Ctrl+Shift+G)", editor.UngroupSelected);

        Act(
            "delete-group",
            Glyphs.Delete(),
            $"Delete the box and the {group.Members.Count} modules in it  (Delete)",
            editor.DeleteSelected);

        plate.Under.Children.Insert(above, actions);

        // One heading and a row per socket, each named for the module and port
        // inside that it stands for — which is exactly what the box draws, so
        // the panel and the canvas read the same.
        void Edge(string heading, IReadOnlyList<GroupSocket> sockets)
        {
            if (sockets.Count == 0) return;

            inspector.Children.Add(new TextBlock
            {
                Text = heading,
                FontSize = Text.Small,
                FontWeight = FontWeight.SemiBold,
                Opacity = 0.7,
                Margin = new Thickness(0, 10, 0, 2),
            });

            foreach (var socket in sockets)
                if (editor.Named(socket) is var (label, spec))
                    inspector.Children.Add(Socket(socket, label, spec));
        }

        // A row, and — on one with nothing plugged into it — the way to take it
        // off the edge again. Only there while it is unwired: a socket a wire is
        // on comes back the moment anything asks, so a button offering to remove
        // one would appear to do nothing.
        Control Socket(GroupSocket socket, string label, PortSpec spec)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 1) };

            var text = new TextBlock
            {
                Text = label,
                FontSize = Text.Body,
                Opacity = 0.85,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Colors.PortColor(spec.Kind)),
            };

            // A label cut short here is usually a formula, whole in the tooltip.
            ToolTip.SetTip(text, label);

            if (!editor.Patch.Wired(group, socket) && !editor.Locked)
            {
                var remove = new Button
                {
                    Content = "✕",
                    FontSize = Text.Caption,
                    Padding = new Thickness(5, 0, 5, 0),
                    Background = Brushes.Transparent,
                    Opacity = 0.55,
                    VerticalAlignment = VerticalAlignment.Center,
                };

                ToolTip.SetTip(remove, "Take this socket off the edge. A wire puts it back.");
                remove.Click += (_, _) => editor.HideSocket(group, socket);

                DockPanel.SetDock(remove, Dock.Right);
                row.Children.Add(remove);
            }

            row.Children.Add(text);
            return row;
        }

        Button Act(string name, Control icon, string tip, Action gesture)
        {
            var button = Drawn(name, icon, tip);

            button.Click += (_, _) => gesture();
            actions.Children.Add(button);

            return button;
        }
    }

    /// <summary>
    /// Keeps a group in the module list, asking first where doing so would replace
    /// one already kept under that name.
    /// </summary>
    /// <remarks>
    /// Replacing is somebody's group going for good, with nothing on this side of
    /// it to undo, and a name typed a second time by accident is the ordinary way
    /// to lose one. Asked in the place the button was standing — the row of them
    /// gives way to the question — the way the module list asks about a row that is
    /// going: a dialog would be right if this could lose work, and what it can lose
    /// is one entry in a list.
    /// </remarks>
    private void KeepGroup(NodeGroup group, StackPanel actions)
    {
        if (groups is null || string.IsNullOrWhiteSpace(group.Name)) return;

        var host = actions.Parent as Panel;
        var at = host?.Children.IndexOf(actions) ?? -1;

        // Nothing kept under that name, or no row left to ask in — either way
        // there is nothing to ask about.
        if (groups.Named(group.Name) is null || host is null || at < 0)
        {
            SaveGroup(group);
            return;
        }

        host.Children[at] = Question.Row(
            $"Replace “{group.Name}”?",
            actions.Margin,
            $"Replace the kept “{group.Name}” with this group.",
            "Leave the kept one alone.",
            replace =>
            {
                if (replace) SaveGroup(group);

                // Put back exactly what a fresh panel would have, which is the
                // button saying whatever it should say now — a replaced group is
                // one this list already knows, so its tip changes.
                BuildInspector();
            });
    }

    private Control BuildGroupTitle(NodeGroup group, IBrush ink)
    {
        var title = new TextBlock
        {
            Text = group.Title(),
            FontSize = Text.Title,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Right,
            Foreground = ink,
            Background = Brushes.Transparent,
        };

        ToolTip.SetTip(title, group.Name is null
            ? "Double-click to give this group a name of its own."
            : $"Double-click to rename. Empty the box to go back to '{group.Counted}'.");

        title.Cursor = NameBox.Renaming;

        title.DoubleTapped += (_, e) =>
        {
            e.Handled = true;

            NameBox.Open(
                title,
                ink,
                group.Name,
                group.Counted,
                NodeGroup.NameLimit,
                typed => group.Rename(typed),
                () => group.Name,
                () => BuildGroupTitle(group, ink),
                () => editor.NotifyPatchChanged());
        };

        return title;
    }

    /// <summary>
    /// The name at the top of the panel, which a double-click turns into a box to
    /// type another one into.
    /// </summary>
    /// <remarks>
    /// A module is a thing on a canvas before it is a type, and a patch with four
    /// Mixers in it is one you have to follow a wire to read. The name is only ever
    /// a label: nothing is found by it. Transparent rather than unpainted, because
    /// a <see cref="TextBlock"/> with no background is not there as far as the
    /// pointer is concerned.
    /// </remarks>
    private Control BuildTitle(NodeInstance node, NodeDef def, IBrush ink)
    {
        var title = new TextBlock
        {
            Text = node.Title(def),
            FontSize = Text.Title,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Right,
            Foreground = ink,
            Background = Brushes.Transparent,
        };

        // A name on a source-built module is the name the `let` gave it, so
        // renaming here would be renaming the wrong copy — the text would put
        // the old one back on the next apply.
        if (editor.Locked)
        {
            ToolTip.SetTip(title, "The text names this module. Rename it there.");
            return title;
        }

        ToolTip.SetTip(title, node.Name is null
            ? "Double-click to give this module a name of its own."
            : $"Double-click to rename. Empty the box to go back to '{def.Name}'.");

        // A name that can be changed says so under the pointer. Only here, because
        // a locked canvas's name is the text's and this one is not a button.
        title.Cursor = NameBox.Renaming;

        title.DoubleTapped += (_, e) =>
        {
            e.Handled = true;
            BeginRename(node, def, ink, title);
        };

        return title;
    }

    /// <summary>
    /// Swaps the name for a box to type one into. Enter keeps what was typed and so
    /// does clicking away; Escape abandons it; an empty box puts the module back to
    /// its definition's name.
    /// </summary>
    /// <remarks>
    /// The one place here that swaps a control in rather than rebuilding the panel:
    /// the box has to take the keyboard the moment it appears, which means being in
    /// the tree already, and it puts itself back from inside its own
    /// <c>LostFocus</c>.
    /// </remarks>
    private void BeginRename(NodeInstance node, NodeDef def, IBrush ink, Control title) =>
        NameBox.Open(
            title,
            ink,
            node.Name,
            def.Name,
            NodeInstance.NameLimit,
            typed => node.Rename(def, typed),
            () => node.Name,
            () => BuildTitle(node, def, ink),
            () => editor.NotifyPatchChanged());

    /// <summary>
    /// The settings window's Graphics section: what the picture is rendered at,
    /// how often, and by what.
    /// </summary>
    /// <remarks>
    /// Built once and kept, not rebuilt per opening: these controls hold live
    /// state, and a control may have one parent at a time.
    /// </remarks>
    private void BuildGraphicsSection()
    {
        ToolTip.SetTip(previewFrameRate,
            "How often the preview redraws itself. Lower to see it near what a recording will "
            + "show, or to ease off a slow machine — the Recording section picks a take's own "
            + "rate, and reads whatever the preview last drew whatever this says.");

        // What it lists is ShowStartupPatch's, which knows what was chosen.
        ToolTip.SetTip(defaultPreset,
            "Which preset the window opens on the next time it starts. Picking one on the "
            + "toolbar right now does not change this — it only changes what is on the canvas.");

        graphicsSection.Children.Add(InspectorRows.Field("Size", resolution));
        graphicsSection.Children.Add(InspectorRows.Field("Preview rate", previewFrameRate));
        graphicsSection.Children.Add(InspectorRows.Field("Render", gpuButton));
        graphicsSection.Children.Add(InspectorRows.Field("Startup patch", defaultPreset));
    }

    /// <summary>
    /// The settings window's Recording section: how a take begins, and what it is
    /// written as.
    /// </summary>
    /// <remarks>
    /// In the order a take happens — counted in, put back to zero, then written —
    /// so the two rows about the moment Record is pressed are not read as
    /// properties of the file (ADR-0091).
    /// </remarks>
    private void BuildRecordingSection()
    {
        ToolTip.SetTip(countIn,
            "How long the status bar counts down after you have named the file, before "
            + "the recording starts. Ctrl+R during the count calls it off.");
        ToolTip.SetTip(rewindBeforeTake,
            "Take the patch back to zero seconds as the recording starts, so a take begins "
            + "where the patch does. Switch it off to record a session as it stands.");

        ToolTip.SetTip(frameRate, "Frames a second in a recorded video. Takes the next recording, not one already running.");
        ToolTip.SetTip(jpegQuality,
            "How good the picture in a recorded video is: higher looks better and makes a bigger "
            + "file. Read as a JPEG quality by the AVI written here, and as a rate factor by every "
            + "format ffmpeg writes.");

        recordingSection.Children.Add(InspectorRows.Field("Count-in", countIn));
        recordingSection.Children.Add(rewindBeforeTake);

        recordingSection.Children.Add(InspectorRows.Field("Frame rate", frameRate));
        recordingSection.Children.Add(InspectorRows.Field("Quality", jpegQuality));

        BuildEncodingRows(recordingSection);
    }

    /// <summary>
    /// The settings window's Sound section: what the sound backend declares, and the
    /// latency, which every backend is asked for.
    /// </summary>
    private void BuildSoundSection()
    {
        ToolTip.SetTip(latency,
            "How far behind the patch the speakers may run. Lower answers a key sooner; "
            + "raise it if the sound crackles. Flyback asks this of every backend, "
            + "whichever plugin is playing.");

        soundNote.Text = plugins.PreferredAudioOutput is { } output
            ? Attributed($"Played by {output.Name}", plugins.Provider(output))
            : "No sound plugin is installed, so nothing plays. See About for where plugins are looked for.";

        // The note first, so the rows under it are read as the backend's answers
        // rather than Flyback's; then the form, because which device plays is the
        // question people come to this tab with. Nothing here knows what the
        // backend will ask (ADR-0085).
        soundSection.Children.Add(soundNote);

        if (plugins.PreferredAudioOutput is not null) soundSection.Children.Add(soundForm);

        soundSection.Children.Add(InspectorRows.Field("Latency", latency));
    }

    /// <summary>
    /// What a settings tab says about the backend behind it, with the plugin that
    /// offered it named: a machine with two sound plugins installed has nothing
    /// else to say which one these rows belong to.
    /// </summary>
    /// <param name="plugin">
    /// Null for a backend no plugin registered, which leaves the sentence as it
    /// came — an id in brackets that names nothing is worse than no id.
    /// </param>
    internal static string Attributed(string what, PluginInfo? plugin) =>
        plugin is null ? $"{what}." : $"{what}, from the {plugin.Name} plugin ({plugin.Id}).";

    /// <summary>
    /// What is driving this module's unpatched sockets, and why nothing on the
    /// canvas shows it. Null where every socket is either patched or on a knob.
    /// </summary>
    /// <remarks>
    /// The absence is the part that needs explaining: everything else in this
    /// editor is visible in the patch, and a signal arriving from nowhere is not.
    /// Sockets that have since been patched drop out of the list, and
    /// <see cref="InspectorShape.Of"/> already counts a wire arriving as a reason to
    /// rebuild.
    /// </remarks>
    private Control? BuildNormalledNote(NodeInstance node, NodeDef def)
    {
        var reading = new List<string>();

        for (var i = 0; i < def.Inputs.Count; i++)
        {
            if (editor.Patch.IncomingTo(node.Id, i) is not null) continue;
            if (NodeCatalog.Normalled(def.Inputs[i]) is not { } source) continue;

            reading.Add($"'{def.Inputs[i].Name}' is reading {source}");
        }

        if (reading.Count == 0) return null;

        return new TextBlock
        {
            Text = Sentence(reading)
                 + ", with no wire to show for it: the module behind that is hidden, and one of "
                 + "it is shared by the whole patch. It is the unplugged jack of a rack, already "
                 + "carrying the signal you would have plugged in. Patch the socket to read "
                 + "something else instead — unplug it again and this comes back.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Text.Muted,
            FontSize = Text.Body,
            Margin = new Thickness(0, 0, 0, 6),
        };

        // "a", "a and b", "a, b and c" — an inspector is prose, and a list
        // joined with commas to the end reads as a table that lost its rules.
        static string Sentence(List<string> parts) => parts.Count switch
        {
            1 => parts[0],
            2 => $"{parts[0]} and {parts[1]}",
            _ => $"{string.Join(", ", parts[..^1])} and {parts[^1]}",
        };
    }

    /// <summary>
    /// Which control edits one of the things a module carries that is not a knob.
    /// </summary>
    /// <remarks>
    /// The one place the App knows the kinds apart, and a lookup here rather than a
    /// method on <see cref="NodeExtra"/> because the engine does not reference
    /// Avalonia. A kind with nothing here costs a row on the panel and not the
    /// panel.
    /// </remarks>
    private Control? EditorFor(NodeExtra extra, NodeInstance node, NodeDef def, bool reading) => extra switch
    {
        // A sequencer's tune is a list rather than a row of knobs (ADR-0038),
        // so it is edited as one — added to, taken from and reordered.
        StepsExtra steps => new StepList(node, steps.Spec, because => Edited(node, because)).View,

        // A quantiser's scale is a set rather than a sequence, so it is edited
        // as the octave it is a subset of rather than as a list of numbers.
        ScaleExtra => new ScaleKeys(node, def, because => Edited(node, because)).View,

        // The one a node carries that is not a number, so it is a name and a
        // button rather than a control with a range.
        SampleExtra => BuildSampleRow(node),
        PictureExtra => BuildPictureRow(node),

        // Anything else is a plugin's own kind, which ships no control and is
        // drawn from what it declares instead — see ADR-0055. A kind that
        // declares nothing simply gets no rows.
        _ => BuildDeclaredRows(node, extra, reading),
    };

    /// <summary>
    /// How the computer keyboard is laid out, on a MIDI In that listens to it.
    /// </summary>
    /// <remarks>
    /// The patch's setting rather than the module's (ADR-0099), shown here
    /// because this is where somebody playing the keys is looking. Every MIDI In
    /// on the keyboard shows the same one, and the heading says so, so that
    /// changing it on one and finding it changed on another is what was
    /// expected. Not shown on a module listening to a device, whose notes are
    /// its own.
    /// </remarks>
    private Control? BuildKeyboardSection(NodeInstance node, NodeDef def)
    {
        if (node.TypeId != NodeCatalog.MidiTypeId) return null;

        var device = new ExtraState(new MidiExtra().Fields, node.StateOf(MidiExtra.StateKey)).Chosen(MidiExtra.DeviceField);

        if (!string.IsNullOrWhiteSpace(device) && device != MidiSources.Keyboard) return null;

        var panel = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };

        panel.Children.Add(new TextBlock
        {
            Text = "computer keyboard — the whole patch's, the same on every MIDI In",
            FontSize = Text.Micro,
            Foreground = Text.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4),
        });

        var layout = new ExtraField.Choice(
            "layout",
            "layout",
            [new ChoiceOption(Piano, "Piano"), new ChoiceOption(ByScale, "Scale")],
            Piano);

        panel.Children.Add(Rows.ChoiceRow(
            layout,
            editor.Patch.KeyboardScale is null ? Piano : ByScale,
            picked =>
            {
                // A scale left behind is picked up again, so trying the piano
                // for a moment does not cost the notes that had been chosen.
                if (picked == ByScale) editor.Patch.KeyboardScale = [.. (IEnumerable<int>?)keptKeyboardScale ?? Major];
                else
                {
                    keptKeyboardScale = editor.Patch.KeyboardScale;
                    editor.Patch.KeyboardScale = null;
                }

                Relaid();

                // After the picker has finished with its own event, since what
                // is rebuilt includes the picker.
                Dispatcher.UIThread.Post(BuildInspector);
            }));

        if (editor.Patch.KeyboardScale is not null)
            panel.Children.Add(new ScaleKeys(
                def.Category,
                () => [.. editor.Patch.KeyboardScale ?? []],
                scale =>
                {
                    editor.Patch.KeyboardScale = scale;
                    Relaid();
                    editor.NotifyPatchChanged();
                },
                played: true).View);

        return panel;
    }

    private const string Piano = "piano";
    private const string ByScale = "scale";

    /// <summary>What a fresh scale layout starts on: C major, what a fresh Quantiser starts on.</summary>
    private static readonly int[] Major = [0, 2, 4, 5, 7, 9, 11];

    /// <summary>The scale last switched away from, for switching back to.</summary>
    private List<int>? keptKeyboardScale;

    private InspectorRows? rows;

    /// <summary>The panel's editable rows, which report an edit to the canvas and the hand coming off to the text.</summary>
    private InspectorRows Rows => rows ??= new InspectorRows(because => editor.NotifyPatchChanged(because), HandCameOff);

    /// <summary>
    /// A plugin's extra, drawn from its <see cref="NodeExtra.Fields"/>.
    /// </summary>
    /// <remarks>
    /// Knowledge of the vocabulary rather than of any plugin: nothing here could
    /// tell you which one it is drawing. A field shape this build has never heard
    /// of is skipped rather than drawn wrongly.
    /// </remarks>
    private Control? BuildDeclaredRows(NodeInstance node, NodeExtra extra, bool reading)
    {
        if (extra.Fields.Count == 0) return null;

        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

        panel.Children.Add(new TextBlock
        {
            Text = extra.Key,
            FontSize = Text.Micro,
            Foreground = Text.Muted,
            Margin = new Thickness(0, 0, 0, 4),
        });

        foreach (var field in extra.Fields)
            if (BuildFieldRow(node, extra, field, reading) is { } control)
                panel.Children.Add(control);

        return panel;
    }

    private Control? BuildFieldRow(NodeInstance node, NodeExtra extra, ExtraField field, bool reading) => field switch
    {
        ExtraField.Number number => Rows.ValueRow(
            field.Label,
            number.Spec,
            number.Value(node.StateOf(extra.Key)?[field.Key]),
            $"{node.Id} {extra.Key} {field.Key}",
            next => Store(node, extra, field, JsonValue.Create(next)),
            reading),

        ExtraField.Toggle toggle => Rows.ToggleRow(
            field.Label,
            toggle.Value(node.StateOf(extra.Key)?[field.Key]),
            next => Store(node, extra, field, JsonValue.Create(next))),

        ExtraField.Choice choice => Rows.ChoiceRow(
            choice,
            choice.Value(node.StateOf(extra.Key)?[field.Key]),
            next =>
            {
                Store(node, extra, field, JsonValue.Create(next));

                // The keyboard's section belongs to a MIDI In on the keyboard,
                // so it comes and goes with the device.
                if (extra is MidiExtra && field.Key == MidiExtra.DeviceField) Dispatcher.UIThread.Post(BuildInspector);
            },

            // What the same field would say if asked again. An extra is free to
            // compute its fields afresh — MidiExtra does, because what it lists
            // is what is plugged in — and this is how the list gets a second
            // chance to be right without the panel being rebuilt.
            () => extra.Fields
                .OfType<ExtraField.Choice>()
                .FirstOrDefault(again => again.Key == field.Key)?.Options ?? choice.Options),

        ExtraField.Text text => Rows.TextRow(
            text,
            text.Value(node.StateOf(extra.Key)?[field.Key]),
            next => Store(node, extra, field, JsonValue.Create(next)),

            // An Expression's formula is the one field whose text is a language,
            // so it is the one that can be marked as unread.
            node.TypeId == NodeCatalog.ExpressionTypeId ? NodeCatalog.FormulaProblem : null),

        _ => null,
    };

    /// <summary>
    /// Writes one field of a plugin's extra back, making the stored object first
    /// where the module arrived without one.
    /// </summary>
    /// <remarks>
    /// Through the field's own tidying, which is where an extra's range differs
    /// from a knob's: a socket's <see cref="PortSpec.Min"/> is the editor's
    /// suggestion and a saved value outside it widens the slider, where a field's
    /// range is what the value means.
    /// </remarks>
    private void Store(NodeInstance node, NodeExtra extra, ExtraField field, JsonNode value)
    {
        var held = extra.Stored(node.StateOf(extra.Key));
        held[field.Key] = field.Sane(value);

        node.SetState(extra.Key, held);

        // Noted rather than written, for the reason a knob is: a field on a
        // slider is dragged, and the text should be edited once at the end of it.
        Restated(node.Id, field.Key);
    }

    /// <summary>
    /// The sound file a player reads: what it is called, and a button to pick
    /// another.
    /// </summary>
    /// <remarks>
    /// The name alone rather than the whole path, with the full one on the tooltip
    /// — a file that has gone is found again by knowing where it was supposed to
    /// be. Nothing here says whether it could be read: that is the compiler's to
    /// say, in the status bar, naming the module.
    /// </remarks>
    private Control BuildSampleRow(NodeInstance node) => BuildFileRow(
        node,
        "file",
        SampleExtra.Of(node),
        "Choose a sound",
        SoundFileType,
        picked =>
        {
            SampleExtra.Set(node, picked);

            // Forgotten first, so a file that has been replaced since it was
            // last read is read again rather than answered from the cache.
            soundFolder.Forget(picked);
        });

    /// <summary>The same row for the other kind of file — see <see cref="PictureExtra"/>.</summary>
    private Control BuildPictureRow(NodeInstance node) => BuildFileRow(
        node,
        "picture",
        PictureExtra.Of(node),
        "Choose a picture",
        PictureFileType,
        picked =>
        {
            PictureExtra.Set(node, picked);
            pictureFolder.Forget(picked);
        });

    /// <summary>
    /// A file this instance carries: what it is called, what it currently is, and a
    /// button that goes and finds another. One row for both kinds, which differ in
    /// the picker's title, the label, the filter and what to do with what comes
    /// back.
    /// </summary>
    private Control BuildFileRow(
        NodeInstance node,
        string label,
        string? held,
        string title,
        FilePickerFileType kind,
        Action<string> store)
    {
        var chosen = held ?? string.Empty;

        var row = InspectorRows.Row("*,Auto");
        row.Margin = new Thickness(0, 8, 0, 0);

        var caption = InspectorRows.Caption(label);

        var name = new TextBlock
        {
            Text = chosen.Length == 0 ? "none chosen" : Path.GetFileName(chosen),
            FontSize = Text.Body,
            Opacity = chosen.Length == 0 ? 0.45 : 0.75,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(4, 0),
        };

        if (chosen.Length > 0) ToolTip.SetTip(name, chosen);

        var choose = new Button { Content = "Choose…", FontSize = Text.Small };

        choose.Click += async (_, _) =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = [kind],
            });

            if (files.Count == 0 || files[0].TryGetLocalPath() is not { } picked) return;

            store(picked);

            // The shape of the panel has not changed, so it is not rebuilt — the
            // one row that did is written here, the way a knob writes its own.
            name.Text = Path.GetFileName(picked);
            name.Opacity = 0.75;
            ToolTip.SetTip(name, picked);

            Edited(node);

            // Every other control in the panel is written into the text by the
            // hand coming off it, and the hand came off this button before the
            // dialog opened: the file arrives after that release, with nothing
            // left to flush it. Said here, because the gesture is over the
            // moment the picker answers.
            HandCameOff();
        };

        Grid.SetColumn(caption, 0);
        Grid.SetColumn(name, 1);
        Grid.SetColumn(choose, 2);

        row.Children.Add(caption);
        row.Children.Add(name);
        row.Children.Add(choose);

        return row;
    }

    /// <summary>What the sound picker offers, which is what the reader can read.</summary>
    private static FilePickerFileType SoundFileType => new("WAV audio")
    {
        Patterns = ["*.wav"],
        MimeTypes = ["audio/wav", "audio/x-wav"],
    };

    /// <summary>And what the picture picker offers, for the same reason.</summary>
    private static FilePickerFileType PictureFileType => new("PNG images")
    {
        Patterns = ["*.png"],
        MimeTypes = ["image/png"],
    };

    private Control BuildInputRow(NodeDef def, NodeInstance node, PortSpec spec, int index, bool reading)
    {
        var connected = editor.Patch.IncomingTo(node.Id, index) is not null;

        var label = InspectorRows.Caption(spec.Name);

        var row = InspectorRows.KnobRow(reading);
        Grid.SetColumn(label, 0);
        row.Children.Add(label);

        if (connected)
        {
            var wired = new TextBlock
            {
                Text = "◀ patched",
                FontSize = Text.Body,
                Foreground = Text.Muted,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(wired, 1);
            Grid.SetColumnSpan(wired, reading ? 3 : 2);
            row.Children.Add(wired);
            return row;
        }

        // A normalled socket has no knob to show. It is already carrying a
        // signal — one the patch does not draw, because there is no module on
        // the canvas for a wire to come from — so the row says which, in the
        // place a slider would have been. Why it is not a slider is the whole
        // point of it: there is nothing to set here until something is patched
        // in, and a control that did nothing would be worse than none.
        if (NodeCatalog.Normalled(spec) is { } normalled)
        {
            var implied = new TextBlock
            {
                Text = $"◀ {normalled}, without a wire",
                FontSize = Text.Body,
                Foreground = Text.Muted,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(implied, 1);
            Grid.SetColumnSpan(implied, reading ? 3 : 2);
            row.Children.Add(implied);
            return row;
        }

        // The other kind of normalled jack: an earlier socket on the same
        // module rather than a hidden one off it. Output's 'right' falls back
        // to 'left' this way, and the row names it exactly as it would a
        // module normalled off the canvas.
        if (spec.NormalledFrom is >= 0 and var from && from < def.Inputs.Count)
        {
            var implied = new TextBlock
            {
                Text = $"◀ {def.Inputs[from].Name}, without a wire",
                FontSize = Text.Body,
                Foreground = Text.Muted,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(implied, 1);
            Grid.SetColumnSpan(implied, reading ? 3 : 2);
            row.Children.Add(implied);
            return row;
        }

        // A socket with nothing worth a knob — see PortSpec.NeedsAWire. The
        // stored default still answers the compiler when nothing is patched,
        // it is just not a number anybody chose by dragging, so the row says
        // that plainly instead of offering a slider that would mislead.
        if (spec.NeedsAWire)
        {
            var unpatched = new TextBlock
            {
                Text = "◀ not patched",
                FontSize = Text.Body,
                Foreground = Text.Muted,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(unpatched, 1);
            Grid.SetColumnSpan(unpatched, reading ? 3 : 2);
            row.Children.Add(unpatched);
            return row;
        }

        if (ControlMap.Of(node, index) is { } link && editor.Patch.Control(link.Control) is { } knob)
            return LinkedRow(node, spec, index, link, knob);

        var value = index < node.InputValues.Length ? node.InputValues[index] : spec.Default;

        // Named after the socket, so a slider dragged across its range is one
        // step to undo rather than one per frame of the drag.
        return Rows.ValueRow(spec.Name, spec, value, $"{node.Id} input {index}", next =>
        {
            if (index < node.InputValues.Length) node.InputValues[index] = next;

            // Noted rather than written. A drag is a knob turned a hundred times
            // and the text should be edited once, when the hand comes off it.
            Turned(node.Id, index);
        }, reading);
    }
}
