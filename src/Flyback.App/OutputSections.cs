using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Flyback.Plugins;
using Flyback.Plugins.Hosting;

namespace Flyback.App;

/// <summary>
/// The Graphics, Recording and Sound sections of the settings window, and the MIDI
/// section's two rows about knobs and keys: every control an
/// <see cref="OutputSettings"/> is shown in and read back from (ADR-0148).
/// </summary>
/// <remarks>
/// Built once and kept, not rebuilt per opening: these controls hold live state
/// (ADR-0082), and a control may have one parent at a time. They act on nothing
/// themselves. Picking a size or flicking a switch is a draft until Save, and what
/// the window does with what <see cref="Read"/> hands back is its own business.
/// </remarks>
internal sealed class OutputSections
{
    private readonly IFilePickers pickers;
    private readonly IMonitors monitors;
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

    public const string GpuTip =
        "Draw the picture with a shader on the GPU, or on the CPU. Switch to the CPU to " +
        "compare the two, or if a long session starts to look stepped.";

    private readonly PluginCatalog plugins;
    private readonly Lazy<PresetSlot> presets;

    /// <summary>Size, preview rate, renderer, full screen and the startup patch.</summary>
    public StackPanel Graphics { get; } = new() { Spacing = 8, Width = 280 };

    /// <summary>How a take begins, and what it is written as.</summary>
    public StackPanel Recording { get; } = new() { Spacing = 8, Width = 280 };

    /// <summary>Whatever the sound backend declares, then how far behind the patch the speakers may run.</summary>
    public StackPanel Sound { get; } = new() { Spacing = 8, Width = 280 };

    /// <summary>What size the picture is drawn at. A take grays it out while it runs.</summary>
    public ComboBox Resolution { get; } = new Picker
    {
        ItemsSource = Resolutions.All.Select(r => r.Label).ToList(),
        SelectedIndex = Resolutions.Default,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>
    /// GPU or CPU. On by default, because it is the one that keeps up with a large
    /// patch, and grayed out by a GPU that turns out to be unusable.
    /// </summary>
    public ComboBox Gpu { get; } = new Picker
    {
        Name = "render",
        ItemsSource = new[] { "GPU", "CPU" },
        SelectedIndex = 0,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>How a knob meets a controller that disagrees with it — the MIDI section.</summary>
    public ComboBox Takeover { get; } = new Picker
    {
        Name = "takeover",
        ItemsSource = new[] { "Jump to the controller", "Pick up the knob" },
        SelectedIndex = 0,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>How a new patch lays out the computer's keyboard — the MIDI section.</summary>
    public ComboBox KeyboardLayout { get; } = new Picker
    {
        Name = "keyboardLayout",
        ItemsSource = new[] { "Piano", "Scale" },
        SelectedIndex = 0,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    private readonly ComboBox frameRate = new Picker
    {
        Name = "frameRate",
        ItemsSource = FrameRates.Select(r => $"{r:0.##} fps").ToList(),
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    private readonly ComboBox previewFrameRate = new Picker
    {
        Name = "previewFrameRate",
        ItemsSource = PreviewFrameRates.Select(r => r <= 0 ? "Unlimited" : $"{r:0.##} fps").ToList(),
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>
    /// Which preset the window opens on at the next launch (ADR-0093). It reads
    /// <see cref="startupPatch"/>, and a click picks another from the gallery.
    /// </summary>
    private readonly Button defaultPreset = new()
    {
        Name = "defaultPreset",
        HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>The name on <see cref="defaultPreset"/>.</summary>
    private readonly TextBlock defaultPresetName = new()
    {
        Name = "defaultPresetName",
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>The name <see cref="defaultPreset"/> shows, and what Save writes.</summary>
    private string startupPatch = "";

    /// <summary>Which monitor full screen fills.</summary>
    private readonly ComboBox fullScreenOn = new Picker
    {
        Name = "fullScreenOn",
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>The monitors <see cref="fullScreenOn"/> lists after its first two rows, in order.</summary>
    private List<MonitorSpot> fullScreenMonitors = [];

    private readonly NumericUpDown jpegQuality = new()
    {
        Name = "jpegQuality",
        Minimum = OutputSettings.LowestQuality,
        Maximum = OutputSettings.HighestQuality,
        Increment = 5,
        FormatString = "0",
        FontSize = Text.Body,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>
    /// How long a take is counted in for — the length ADR-0090 fixed at three
    /// seconds and ADR-0091 made a choice.
    /// </summary>
    private readonly ComboBox countIn = new Picker
    {
        Name = "countIn",
        ItemsSource = CountIns.Select(s => s <= 0 ? "None" : $"{s} s").ToList(),
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>
    /// Whether a take starts at nought seconds. Its own row rather than another
    /// entry on <see cref="countIn"/>, because standing ready and starting from
    /// the beginning are two different things: a take may want either alone.
    /// </summary>
    private readonly CheckBox rewindBeforeTake = new()
    {
        Name = "rewindBeforeTake",
        Content = "Rewind to zero first",
        FontSize = Text.Body,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly ComboBox videoFormat = new Picker
    {
        Name = "videoFormat",
        ItemsSource = ClipFormats.Pictures.Select(f => f.Label).ToList(),
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    private readonly ComboBox soundFormat = new Picker
    {
        Name = "soundFormat",
        ItemsSource = ClipFormats.Sounds.Select(f => f.Label).ToList(),
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    private readonly TextBox ffmpegBox = new()
    {
        Name = "ffmpeg",
        PlaceholderText = "on PATH",
        FontSize = Text.Body,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>What the search found, under the box. Filled in by <see cref="ShowFfmpegAsync"/> and nowhere else.</summary>
    private readonly TextBlock ffmpegNote = new()
    {
        Name = "ffmpegNote",
        FontSize = Text.Small,
        Foreground = Text.Muted,
        TextWrapping = TextWrapping.Wrap,
    };

    private readonly ComboBox latency = new Picker
    {
        Name = "latency",
        ItemsSource = Latencies.Select(ms => $"{ms} ms").ToList(),
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>
    /// The sound backend's own settings — which device plays, for one — drawn from
    /// what it declares (ADR-0085). Empty where no backend is installed or it has
    /// nothing to ask.
    /// </summary>
    private readonly SettingsForm soundForm = new() { Name = "soundForm", Beside = true };

    /// <summary>Which backend plays, and which plugin it came from, above the rows it asks for.</summary>
    private readonly TextBlock soundNote = new()
    {
        Name = "soundNote",
        FontSize = Text.Small,
        Foreground = Text.Muted,
        TextWrapping = TextWrapping.Wrap,
    };

    /// <summary>
    /// What the sections were last saved as: what is in force, and what closing the
    /// settings window without Save puts them back to.
    /// </summary>
    public OutputSettings Saved { get; set; } = new();

    /// <param name="setup">Where the settings are kept.</param>
    /// <param name="presets">The presets the startup patch is named and picked from.</param>
    public OutputSections(PluginCatalog plugins, EditorSetup setup, Lazy<PresetSlot> presets, IFilePickers pickers, IMonitors monitors)
    {
        this.pickers = pickers;
        this.monitors = monitors;
        this.plugins = plugins;
        this.presets = presets;

        if (setup.OutputSettingsPath is { } path) Saved = OutputSettings.Load(path);

        BuildGraphics();
        BuildRecording();
        BuildSound();
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

    /// <summary>What size <paramref name="settings"/> draws the picture at, or the default for one the list no longer offers.</summary>
    public static PixelSize SizeOf(OutputSettings settings) => Resolutions.All[SizeRow(settings)].Size;

    /// <summary>Puts every control to <paramref name="settings"/>, and nothing else.</summary>
    public void Show(OutputSettings settings)
    {
        Resolution.SelectedIndex = SizeRow(settings);

        // A box grayed out by a GPU that failed shows what is running, which
        // the window's BackendChanged handler already set.
        if (Gpu.IsEnabled) Gpu.SelectedIndex = settings.Gpu ? 0 : 1;

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
        Takeover.SelectedIndex = settings.Takeover == Midi.Takeover.PickUp ? 1 : 0;
        KeyboardLayout.SelectedIndex = settings.Keyboard == Midi.KeyboardLayout.Scale ? 1 : 0;

        if (plugins.PreferredAudioOutput is { } output)
            soundForm.Show(output.Form, settings.SoundOf(output.Id));
    }

    /// <summary>What the controls hold, as settings to put in force.</summary>
    /// <param name="before">What was last saved, which keeps whatever a control cannot say.</param>
    public OutputSettings Read(OutputSettings before)
    {
        // Grayed out for the length of a take, whose file has committed to a size
        // and drops every frame that arrives at another. Graying a box does not
        // take back a row already picked in it — during the count-in, say — so
        // what it holds is not read while it is gray, and the row is put back.
        if (!Resolution.IsEnabled) Resolution.SelectedIndex = SizeRow(before);

        var size = Resolutions.All[Math.Max(Resolution.SelectedIndex, 0)].Size;
        var fullScreen = ReadFullScreen(before);

        var read = new OutputSettings
        {
            Width = size.Width,
            Height = size.Height,
            // A box grayed out by a GPU that failed says nothing about what
            // was wanted, so the last answer is kept for a launch that has one.
            Gpu = Gpu.IsEnabled ? Gpu.SelectedIndex == 0 : before.Gpu,

            DefaultPreset = startupPatch,

            FullScreen = fullScreen.On,
            FullScreenMonitor = fullScreen.Monitor,

            FrameRate = FrameRates[Math.Max(frameRate.SelectedIndex, 0)],
            PreviewFrameRate = PreviewFrameRates[Math.Max(previewFrameRate.SelectedIndex, 0)],

            // An emptied box keeps what was saved rather than becoming nought.
            JpegQuality = jpegQuality.Value is { } quality
                ? Math.Clamp((int)Math.Round(quality), OutputSettings.LowestQuality, OutputSettings.HighestQuality)
                : before.JpegQuality,

            LatencyMilliseconds = Latencies[Math.Max(latency.SelectedIndex, 0)],

            CountInSeconds = CountIns[Math.Max(countIn.SelectedIndex, 0)],
            RewindBeforeTake = rewindBeforeTake.IsChecked == true,

            VideoFormat = Takes.Chosen(ClipFormats.Pictures, videoFormat).Id,
            SoundFormat = Takes.Chosen(ClipFormats.Sounds, soundFormat).Id,

            // Trimmed, because a path pasted in with a space on the end is a
            // path nobody meant and one File.Exists would refuse.
            FfmpegPath = (ffmpegBox.Text ?? string.Empty).Trim(),

            // Every backend's, not only the one showing, so a backend that is not
            // installed this launch keeps what it was set to. A copy, because the
            // backend's answers are about to be written into it and what it held
            // before is still to be compared against.
            Sound = new(before.Sound, StringComparer.Ordinal),

            Takeover = Takeover.SelectedIndex == 1 ? Midi.Takeover.PickUp : Midi.Takeover.Jump,
            Keyboard = KeyboardLayout.SelectedIndex == 1 ? Midi.KeyboardLayout.Scale : Midi.KeyboardLayout.Piano,
        };

        // So an emptied box says what it kept, the next time it is looked at.
        jpegQuality.Value = read.JpegQuality;

        if (plugins.PreferredAudioOutput is { } output) read.RememberSound(output.Id, soundForm.Values);

        return read;
    }

    /// <summary>
    /// Whether the sound backend's answers mean something else in
    /// <paramref name="after"/> than in <paramref name="before"/>, read the way the
    /// backend reads them — so a device picked and then picked back is not a
    /// change, though the bag now holds a key it did not.
    /// </summary>
    public bool SoundChanged(OutputSettings before, OutputSettings after)
    {
        if (plugins.PreferredAudioOutput is not { } output) return false;

        var now = after.SoundOf(output.Id);

        return !output.Form(now).All(field =>
            field.Sane(before.SoundOf(output.Id).All.GetValueOrDefault(field.Key)) == field.Sane(now.All.GetValueOrDefault(field.Key)));
    }

    /// <summary>
    /// Looks for ffmpeg and says what was found, or what the lack of it costs.
    /// </summary>
    /// <remarks>
    /// Asked as the settings window opens rather than kept from the last time:
    /// ffmpeg may have been installed, moved or taken away since. Off the UI
    /// thread, because finding out means running a program and asking it.
    /// </remarks>
    public async Task ShowFfmpegAsync()
    {
        var picked = ffmpegBox.Text ?? string.Empty;

        ffmpegNote.Text = "Looking for ffmpeg…";

        // Trimmed, as Save trims it: what is asked about is what would be kept.
        var typed = picked.Trim();

        var (path, version) = await Task.Run(() =>
        {
            var found = Ffmpeg.Resolve(typed);

            return (found, found is null ? null : Ffmpeg.Version(found));
        });

        // The box may have moved on while the process ran — typed into again, or
        // the window closed and reopened — and an answer about a path nobody is
        // asking about any more is worse than none.
        if ((ffmpegBox.Text ?? string.Empty) != picked) return;

        // The one found is not the one named: looking falls back to PATH without a
        // word, and the version of that one under a path that leads nowhere would
        // read as the path having been taken. A path that was taken comes back as
        // it was given, so no more than the two strings needs comparing.
        var elsewhere = typed.Length > 0
            && path is not null
            && !string.Equals(path, typed, StringComparison.OrdinalIgnoreCase);

        ffmpegNote.Text = (path, version) switch
        {
            (null, _) when typed.Length > 0 => $"There is nothing at {typed}, and no ffmpeg on PATH.",

            (null, _) => "No ffmpeg on PATH. Find it here to write anything but "
                + $"{ClipFormats.MotionJpegAvi.Label} or {ClipFormats.Wav.Label}.",

            _ when elsewhere => $"There is nothing at {typed}, so takes use the one on PATH at {path}"
                + (version is null ? "." : $" — {version}."),

            (_, null) => $"Found {path}, but it would not say what it is.",

            _ when typed.Length > 0 => $"{version}.",

            _ => $"{version}, on PATH at {path}.",
        };
    }

    private void BuildGraphics()
    {
        ToolTip.SetTip(previewFrameRate,
            "How often the preview redraws itself. Lower to see it near what a recording will "
            + "show, or to ease off a slow machine — the Recording section picks a take's own "
            + "rate, and reads whatever the preview last drew whatever this says.");

        ToolTip.SetTip(defaultPreset,
            "Which preset the window opens on the next time it starts. Picking one on the "
            + "toolbar right now does not change this — it only changes what is on the canvas.");

        // Drawn as the pickers above it are, so the row reads as a value to change
        // rather than a button to press, with the mark of a row that opens a window.
        var opens = new TextBlock { Text = "⋯", Foreground = Text.Muted, VerticalAlignment = VerticalAlignment.Center };

        Grid.SetColumn(opens, 1);

        defaultPreset.Content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children = { defaultPresetName, opens },
        };

        defaultPreset.Padding = new Thickness(12, 5, 10, 7);
        defaultPreset.MinHeight = 32;
        defaultPreset.BorderThickness = new Thickness(1);
        defaultPreset.Bind(Button.BackgroundProperty, defaultPreset.GetResourceObservable("ComboBoxBackground"));
        defaultPreset.Bind(Button.BorderBrushProperty, defaultPreset.GetResourceObservable("ComboBoxBorderBrush"));

        defaultPreset.Click += async (_, _) =>
        {
            if (await presets.Value.PickStartupPatchAsync(startupPatch) is { } chosen) ShowStartupPatch(chosen);
        };

        ToolTip.SetTip(fullScreenOn,
            "Where double-clicking the preview puts the picture. On another monitor the editor "
            + "stays where it is, and double-clicking the picture or pressing Esc brings it back.");

        Graphics.Children.Add(InspectorRows.Field("Size", Resolution));
        Graphics.Children.Add(InspectorRows.Field("Preview rate", previewFrameRate));
        Graphics.Children.Add(InspectorRows.Field("Render", Gpu));
        Graphics.Children.Add(InspectorRows.Field("Full screen", fullScreenOn));
        Graphics.Children.Add(InspectorRows.Field("Startup patch", defaultPreset));
    }

    /// <remarks>
    /// In the order a take happens — counted in, put back to zero, then written —
    /// so the two rows about the moment Record is pressed are not read as
    /// properties of the file (ADR-0091).
    /// </remarks>
    private void BuildRecording()
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

        Recording.Children.Add(InspectorRows.Field("Count-in", countIn));
        Recording.Children.Add(rewindBeforeTake);

        Recording.Children.Add(InspectorRows.Field("Frame rate", frameRate));
        Recording.Children.Add(InspectorRows.Field("Quality", jpegQuality));

        BuildEncodingRows();
    }

    /// <summary>
    /// The rows the Recording section ends with: which formats, and which ffmpeg
    /// (ADR-0089). A box left empty means whatever is on <c>PATH</c>.
    /// </summary>
    private void BuildEncodingRows()
    {
        ToolTip.SetTip(videoFormat,
            "What a recorded video is written as. AVI is written by Flyback itself and needs "
            + "nothing installed, at around twenty times the size of an MP4 of the same clip; "
            + "every other row is encoded by ffmpeg.");

        ToolTip.SetTip(soundFormat, "What a recorded sound is written as, when you record the sound on its own.");

        ToolTip.SetTip(ffmpegBox,
            "Which ffmpeg to encode with. Left empty, the first one on PATH is used — "
            + "fill it in only if that is not the one you mean.");

        // As tall as the box it sits beside, and a step away from it.
        var browse = new Button
        {
            Content = "…",
            Width = 32,
            FontSize = Text.Body,
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };

        ToolTip.SetTip(browse, "Find ffmpeg on this machine.");

        browse.Click += async (_, _) => await PickFfmpegAsync();

        // Typed as well as picked, since a path pasted in is the quicker way when
        // you already know it. Looked for as it is typed rather than on Save,
        // because the answer is the whole point of the row.
        ffmpegBox.LostFocus += async (_, _) => await ShowFfmpegAsync();

        // The gutter every other row uses, then the box, then the button — one
        // column more than Field builds, which is why this row is built here.
        var row = InspectorRows.Row("*,Auto", InspectorRows.SettingsGutter);

        var label = InspectorRows.Caption("ffmpeg", InspectorRows.SettingsGutter);

        Grid.SetColumn(label, 0);
        Grid.SetColumn(ffmpegBox, 1);
        Grid.SetColumn(browse, 2);

        row.Children.Add(label);
        row.Children.Add(ffmpegBox);
        row.Children.Add(browse);

        Recording.Children.Add(InspectorRows.Field("Video", videoFormat));
        Recording.Children.Add(InspectorRows.Field("Sound", soundFormat));
        Recording.Children.Add(row);
        Recording.Children.Add(ffmpegNote);
    }

    private void BuildSound()
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
        Sound.Children.Add(soundNote);

        if (plugins.PreferredAudioOutput is not null) Sound.Children.Add(soundForm);

        Sound.Children.Add(InspectorRows.Field("Latency", latency));
    }

    /// <summary>Asks where ffmpeg is, and looks at what was picked.</summary>
    private async Task PickFfmpegAsync()
    {
        var file = await pickers.Open(new FilePickerOpenOptions
        {
            Title = "Find ffmpeg",
            AllowMultiple = false,

            // Named rather than filtered to executables: what an executable is
            // differs per platform, and the one file wanted here is known by name.
            FileTypeFilter = [new FilePickerFileType("ffmpeg") { Patterns = [Ffmpeg.FileName] }],
        });

        if (file is [{ } picked] && picked.TryGetLocalPath() is { } path)
        {
            ffmpegBox.Text = path;

            await ShowFfmpegAsync();
        }
    }

    /// <summary>
    /// Shows which preset the window starts on, naming the one chosen even where
    /// this launch does not offer it.
    /// </summary>
    /// <remarks>
    /// A plugin's preset, on a launch the plugin is away from. The window opened
    /// on the first patch instead, and naming that one here would have the next
    /// Save — of anything at all — write it over the choice. Nothing chosen yet
    /// names the patch the window opens on, which is what saving it then means.
    /// </remarks>
    private void ShowStartupPatch(string chosen)
    {
        var offered = presets.Value.Ordered();

        startupPatch = chosen.Length > 0 ? chosen : offered[PresetLibrary.Opening(offered, chosen)].Name;
        defaultPresetName.Text = startupPatch;
    }

    /// <summary>
    /// Lists the monitors plugged in now, and the chosen one if it is not, and
    /// selects what the saved settings say. Asked as the settings open, since the
    /// monitors are the window's to name.
    /// </summary>
    public void ShowMonitors()
    {
        var settings = Saved;

        var screens = monitors.All;

        fullScreenMonitors = [.. screens.Select(s => MonitorPlacement.Describe(s)!)];

        List<string> rows =
        [
            "Same monitor",
            "Another monitor",
            .. screens.Select(s => $"{s.DisplayName ?? "Monitor"} · {s.Bounds.Width}×{s.Bounds.Height}{(s.IsPrimary ? " · main" : "")}"),
        ];

        var chosen = settings.FullScreenMonitor is { } wanted ? MonitorPlacement.Find(wanted, fullScreenMonitors) : null;

        // Kept on the list while unplugged, so saving anything else does not forget it.
        if (settings.FullScreenMonitor is { } away && chosen is null)
        {
            fullScreenMonitors.Add(away);
            rows.Add($"{away.Name ?? "Monitor"} · {away.Width}×{away.Height} · not plugged in");
            chosen = fullScreenMonitors.Count - 1;
        }

        fullScreenOn.ItemsSource = rows;
        fullScreenOn.SelectedIndex = settings.FullScreen switch
        {
            FullScreenOn.OtherMonitor => 1,
            FullScreenOn.ChosenMonitor when chosen is { } row => 2 + row,
            _ => 0,
        };
    }

    /// <summary>What <see cref="fullScreenOn"/> holds, as the settings keep it.</summary>
    private (FullScreenOn On, MonitorSpot? Monitor) ReadFullScreen(OutputSettings before) => fullScreenOn.SelectedIndex switch
    {
        1 => (FullScreenOn.OtherMonitor, before.FullScreenMonitor),
        >= 2 and var row when row - 2 < fullScreenMonitors.Count => (FullScreenOn.ChosenMonitor, fullScreenMonitors[row - 2]),
        _ => (FullScreenOn.SameMonitor, before.FullScreenMonitor),
    };

    /// <summary>
    /// The row of a list nearest a saved value, so a value written by hand that
    /// the list does not offer shows as the closest one that it does.
    /// </summary>
    private static int Nearest(IReadOnlyList<double> rows, double value) =>
        Enumerable.Range(0, rows.Count).MinBy(row => Math.Abs(rows[row] - value));

    /// <summary>
    /// The row of a format list a saved id is. An id this build does not define
    /// shows as the format written here, which is the first row of either list.
    /// </summary>
    private static int Row(IReadOnlyList<ClipFormat> formats, string? id, bool picture)
    {
        var wanted = ClipFormats.Wanted(id, picture);

        // Nought either way: it is where the format written here sits in both
        // lists, and so is both the answer and the fallback.
        return Enumerable.Range(0, formats.Count).FirstOrDefault(row => formats[row] == wanted);
    }

    /// <summary>The row of the size list a saved size is, or the default for one the list no longer offers.</summary>
    private static int SizeRow(OutputSettings settings)
    {
        var row = Array.FindIndex(Resolutions.All,
            r => r.Size.Width == settings.Width && r.Size.Height == settings.Height);

        return row < 0 ? Resolutions.Default : row;
    }
}
