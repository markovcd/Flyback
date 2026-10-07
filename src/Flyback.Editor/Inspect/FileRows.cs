using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;
using Flyback.Engine.Render;
using Flyback.Ui.Controls;

namespace Flyback.Editor.Inspect;

/// <summary>
/// The inspector's row for a file a module carries: what it is called, what it
/// currently is, and a button that goes and finds another.
/// </summary>
/// <param name="files">The folders the patch reads its sound files and pictures from.</param>
internal sealed class FileRows(IFilePickers pickers, Document document, PatchFiles files)
{
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
    public Control Sample(NodeInstance node) => Row(
        node,
        "file",
        SampleExtra.Of(node),
        "Choose a sound",
        SoundFileType,
        picked =>
        {
            var named = PatchPaths.Named(picked, files.SoundFolder.Library);

            SampleExtra.Set(node, named);

            // Forgotten first, so a file that has been replaced since it was
            // last read is read again rather than answered from the cache.
            files.SoundFolder.Forget(named);
        });

    /// <summary>The same row for the other kind of file — see <see cref="PictureExtra"/>.</summary>
    public Control Picture(NodeInstance node) => Row(
        node,
        "picture",
        PictureExtra.Of(node),
        "Choose a picture",
        PictureFileType,
        picked =>
        {
            var named = PatchPaths.Named(picked, files.PictureFolder.Library);

            PictureExtra.Set(node, named);
            files.PictureFolder.Forget(named);
        });

    /// <summary>The same row for a MIDI file — see <see cref="MidiFileExtra"/>.</summary>
    public Control MidiFile(NodeInstance node) => Row(
        node,
        "midi file",
        MidiFileExtra.Of(node),
        "Choose a MIDI file",
        MidiFileType,
        picked =>
        {
            var named = PatchPaths.Named(picked, files.SoundFolder.Library);

            MidiFileExtra.Set(node, named);
            files.SoundFolder.Forget(named);
        });

    /// <summary>
    /// A file this instance carries: what it is called, what it currently is, and a
    /// button that goes and finds another. One row for both kinds, which differ in
    /// the picker's title, the label, the filter and what to do with what comes
    /// back.
    /// </summary>
    private Control Row(
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
            var files = await pickers.Open(new FilePickerOpenOptions
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

            document.Edited(node);

            // Every other control in the panel is written into the text by the
            // hand coming off it, and the hand came off this button before the
            // dialog opened: the file arrives after that release, with nothing
            // left to flush it. Said here, because the gesture is over the
            // moment the picker answers.
            document.HandCameOff();
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
    private static FilePickerFileType SoundFileType => new("WAV or MP3 audio")
    {
        Patterns = ["*.wav", "*.mp3"],
        MimeTypes = ["audio/wav", "audio/x-wav", "audio/mpeg"],
    };

    /// <summary>And what the MIDI picker offers.</summary>
    private static FilePickerFileType MidiFileType => new("MIDI files")
    {
        Patterns = ["*.mid", "*.midi"],
        MimeTypes = ["audio/midi", "audio/x-midi"],
    };

    /// <summary>And what the picture picker offers, for the same reason.</summary>
    private static FilePickerFileType PictureFileType => new("PNG images")
    {
        Patterns = ["*.png"],
        MimeTypes = ["image/png"],
    };
}
