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
    /// The file a module reads: what it is called, and a button to pick another.
    /// </summary>
    /// <remarks>
    /// The name alone rather than the whole path, with the full one on the tooltip
    /// — a file that has gone is found again by knowing where it was supposed to
    /// be. Nothing here says whether it could be read: that is the compiler's to
    /// say, in the status bar, naming the module.
    /// </remarks>
    public Control Row(NodeInstance node, FileExtra file)
    {
        var (library, forget) = file.Kind.Picture
            ? (files.PictureFolder.Library, (Action<string>)files.PictureFolder.Forget)
            : (files.SoundFolder.Library, files.SoundFolder.Forget);

        return Row(node, file.Kind, file.PathOf(node), picked =>
        {
            var named = PatchPaths.Named(picked, library);

            file.Point(node, named);

            // Forgotten first, so a file that has been replaced since it was
            // last read is read again rather than answered from the cache.
            forget(named);
        });
    }

    /// <summary>The row itself: the caption, the file's name and the button that picks another.</summary>
    private Control Row(NodeInstance node, FileKind kind, string? held, Action<string> store)
    {
        var chosen = held ?? string.Empty;

        var row = InspectorRows.Row("*,Auto");
        row.Margin = new Thickness(0, 8, 0, 0);

        var caption = InspectorRows.Caption(kind.Label);

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
                Title = kind.Choose,
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType(kind.Described) { Patterns = kind.Patterns, MimeTypes = kind.MimeTypes }],
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
}
