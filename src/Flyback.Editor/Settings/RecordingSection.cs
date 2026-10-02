using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Flyback.Editor.Capture;
using Flyback.Editor.Controls;
using Flyback.Ui.Controls;
using Flyback.Editor.Inspect;
using Flyback.Engine.Render;
using Flyback.Ui;

namespace Flyback.Editor.Settings;

/// <summary>The Recording section of the settings window: how a take begins, and what it is written as.</summary>
/// <remarks>
/// In the order a take happens — counted in, put back to zero, then written —
/// so the two rows about the moment Record is pressed are not read as
/// properties of the file (ADR-0091). The video's rate and quality follow its
/// format, since what quality means depends on the format.
/// </remarks>
internal sealed class RecordingSection : ISettingsSection
{
    /// <summary>The frame rates a take can be recorded at: film, PAL, the usual, and the two doubles.</summary>
    private static readonly double[] FrameRates = [24, 25, 30, 50, 60];

    /// <summary>
    /// The counts a take can be counted in for, in seconds, 0 standing for none
    /// — the first row, so <see cref="SettingRows.Nearest"/> does not land a
    /// saved 0 on 1.
    /// </summary>
    private static readonly int[] CountIns = [0, 1, 2, 3, 5, 10];

    private readonly IFilePickers pickers;
    private readonly OutputSettingRepository settings;
    private readonly PatchFiles files;

    public string Name => "Recording";

    public Control View => rows;

    private readonly StackPanel rows = new() { Spacing = 8, Width = 280 };

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

    private readonly ComboBox frameRate = new Picker
    {
        Name = "frameRate",
        ItemsSource = FrameRates.Select(r => $"{r:0.##} fps").ToList(),
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

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

    /// <param name="files">The patch's files, whose MP3 samples are read through the ffmpeg chosen here.</param>
    public RecordingSection(IFilePickers pickers, OutputSettingRepository settings, PatchFiles files)
    {
        this.pickers = pickers;
        this.settings = settings;
        this.files = files;

        ToolTip.SetTip(countIn,
            "How long the status bar counts down after you have named the file, before "
            + "the recording starts. Ctrl+R during the count calls it off.");
        ToolTip.SetTip(rewindBeforeTake,
            "Take the patch back to zero seconds as the recording starts, so a take begins "
            + "where the patch does. Switch it off to record a session as it stands.");

        rows.Children.Add(InspectorRows.Field("Count-in", countIn));
        rows.Children.Add(rewindBeforeTake);

        BuildEncodingRows();
    }

    /// <summary>Looks for ffmpeg afresh, since it may have been installed, moved or taken away since.</summary>
    public void Opening() => _ = ShowFfmpegAsync();

    public void Start()
    {
        Show();
        files.SoundFolder.FfmpegPath = settings.Current.FfmpegPath;
    }

    public void Show() => Show(settings.Current);

    public void Save()
    {
        settings.Change(Read);
        files.SoundFolder.FfmpegPath = settings.Current.FfmpegPath;
    }

    private void Show(OutputSettings current)
    {
        frameRate.SelectedIndex = SettingRows.Nearest(FrameRates, current.FrameRate);
        jpegQuality.Value = current.JpegQuality;

        countIn.SelectedIndex = SettingRows.Nearest(CountIns.Select(s => (double)s).ToArray(), current.CountInSeconds);
        rewindBeforeTake.IsChecked = current.RewindBeforeTake;

        videoFormat.SelectedIndex = Row(ClipFormats.Pictures, current.VideoFormat, picture: true);
        soundFormat.SelectedIndex = Row(ClipFormats.Sounds, current.SoundFormat, picture: false);
        ffmpegBox.Text = current.FfmpegPath;
    }

    /// <summary>Writes what the controls hold into <paramref name="into"/>, keeping a quality whose box was emptied.</summary>
    private void Read(OutputSettings into)
    {
        into.FrameRate = FrameRates[Math.Max(frameRate.SelectedIndex, 0)];

        // An emptied box keeps what was saved rather than becoming nought.
        into.JpegQuality = jpegQuality.Value is { } quality
            ? Math.Clamp((int)Math.Round(quality), OutputSettings.LowestQuality, OutputSettings.HighestQuality)
            : into.JpegQuality;

        // So an emptied box says what it kept, the next time it is looked at.
        jpegQuality.Value = into.JpegQuality;

        into.CountInSeconds = CountIns[Math.Max(countIn.SelectedIndex, 0)];
        into.RewindBeforeTake = rewindBeforeTake.IsChecked == true;

        into.VideoFormat = Takes.Chosen(ClipFormats.Pictures, videoFormat).Id;
        into.SoundFormat = Takes.Chosen(ClipFormats.Sounds, soundFormat).Id;

        // Trimmed, because a path pasted in with a space on the end is a
        // path nobody meant and one File.Exists would refuse.
        into.FfmpegPath = (ffmpegBox.Text ?? string.Empty).Trim();
    }

    /// <summary>
    /// Looks for ffmpeg and says what was found, or what the lack of it costs.
    /// </summary>
    /// <remarks>
    /// Off the UI thread, because finding out means running a program and asking it.
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

    /// <summary>
    /// The rows the section ends with: the video's format, rate and quality, the
    /// sound's format, and which ffmpeg (ADR-0089). A box left empty means
    /// whatever is on <c>PATH</c>.
    /// </summary>
    private void BuildEncodingRows()
    {
        ToolTip.SetTip(videoFormat,
            "What a recorded video is written as. AVI is written by Flyback itself and needs "
            + "nothing installed, at around twenty times the size of an MP4 of the same clip; "
            + "every other row is encoded by ffmpeg.");

        ToolTip.SetTip(frameRate, "Frames a second in a recorded video. Takes the next recording, not one already running.");
        ToolTip.SetTip(jpegQuality,
            "How good the picture in a recorded video is: higher looks better and makes a bigger "
            + "file. Read as a JPEG quality by the AVI written here, and as a rate factor by every "
            + "format ffmpeg writes.");

        ToolTip.SetTip(soundFormat, "What a recorded sound is written as, when you record the sound on its own.");

        ToolTip.SetTip(ffmpegBox,
            "Which ffmpeg to encode with and to read an MP3 sample with. Left empty, the first one on PATH is used — "
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

        rows.Children.Add(InspectorRows.Field("Video format", videoFormat));
        rows.Children.Add(InspectorRows.Field("Frame rate", frameRate));
        rows.Children.Add(InspectorRows.Field("Quality", jpegQuality));
        rows.Children.Add(InspectorRows.Field("Sound format", soundFormat));
        rows.Children.Add(row);
        rows.Children.Add(ffmpegNote);
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
}
