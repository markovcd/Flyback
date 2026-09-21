using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Flyback.App.Controls;
using Flyback.Core.Render;

namespace Flyback.App;

/// <summary>
/// What a take is encoded as, and where the encoder is — the rest of the settings
/// window's Recording section (ADR-0089).
/// </summary>
/// <remarks>
/// Two of the formats this program writes itself and the others are ffmpeg's, so
/// this region is also the only place in the shell that cares whether ffmpeg is
/// on the machine. It is looked for rather than asked about: a box left empty
/// means whatever is on <c>PATH</c>, and is filled in only by somebody who has
/// a particular ffmpeg in mind.
/// </remarks>
public sealed partial class MainWindow
{
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

    /// <summary>
    /// The rows the Recording section ends with: which formats, and which ffmpeg.
    /// </summary>
    private void BuildEncodingRows(StackPanel section)
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
            Margin = new Avalonia.Thickness(4, 0, 0, 0),
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

        section.Children.Add(InspectorRows.Field("Video", videoFormat));
        section.Children.Add(InspectorRows.Field("Sound", soundFormat));
        section.Children.Add(row);
        section.Children.Add(ffmpegNote);
    }

    /// <summary>Asks where ffmpeg is, and looks at what was picked.</summary>
    private async Task PickFfmpegAsync()
    {
        var file = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
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
    /// Looks for ffmpeg and says what was found, or what the lack of it costs.
    /// </summary>
    /// <remarks>
    /// Off the UI thread, because finding out means running a program and asking
    /// it: a walk of <c>PATH</c> and one process, which is nothing to wait for
    /// and too much to wait for on the thread drawing the window.
    /// </remarks>
    private async Task ShowFfmpegAsync()
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
        // read as the path having been taken.
        // A path that was taken comes back as it was given, so no more than the
        // two strings needs comparing.
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

    /// <summary>The format one of the two pickers is on.</summary>
    private static ClipFormat Chosen(IReadOnlyList<ClipFormat> formats, ComboBox picker) =>
        formats[Math.Clamp(picker.SelectedIndex, 0, formats.Count - 1)];

    /// <summary>
    /// The format a take is written as, and the ffmpeg for it. Read from what was
    /// saved rather than from the pickers, since a settings window left open on a
    /// row nobody pressed Save on is not a choice yet.
    /// </summary>
    /// <param name="path">
    /// Where the take is going. Its extension decides, so a name typed over the
    /// picker's suggestion means what it says — see <see cref="TakeFormat"/>.
    /// </param>
    /// <returns>
    /// The format, and null for <c>Ffmpeg</c> where none is needed or none was
    /// found — which the caller has to tell apart before it starts.
    /// </returns>
    private (ClipFormat Format, string? Ffmpeg) Encoder(string path)
    {
        var format = TakeFormat(
            path,
            ClipFormats.Wanted(outputSettings.VideoFormat, picture: true),
            ClipFormats.Wanted(outputSettings.SoundFormat, picture: false));

        return (format, format.NeedsFfmpeg ? Ffmpeg.Resolve(outputSettings.FfmpegPath) : null);
    }

    /// <summary>
    /// The format a file name asks for, given the two the settings are on.
    /// </summary>
    /// <remarks>
    /// An extension two formats share means the one chosen in the settings:
    /// <c>.mp4</c> is H.265 to somebody who picked H.265, and no name could say
    /// so otherwise. Any other extension means the first format that has it, and
    /// one nothing writes means the video format chosen.
    /// </remarks>
    internal static ClipFormat TakeFormat(string path, ClipFormat video, ClipFormat sound)
    {
        var extension = Path.GetExtension(path);

        foreach (var chosen in new[] { video, sound })
        {
            if (extension.Equals(chosen.Extension, StringComparison.OrdinalIgnoreCase)) return chosen;
        }

        return ClipFormats.ByExtension(path) ?? video;
    }
}
