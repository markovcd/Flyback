using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaEdit;

namespace Flyback.Editor.Hubs;

/// <summary>
/// Ctrl+D in the text duplicates, as it does on the canvas and in the Edit menu: the
/// selection after itself, or with nothing selected the caret's line below it.
/// </summary>
/// <remarks>Caught on the way down, ahead of the editor's own Ctrl+D, which deletes the line.</remarks>
internal static class SourceDuplicate
{
    public static void Attach(TextEditor editor) =>
        editor.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.D || e.KeyModifiers is not (KeyModifiers.Control or KeyModifiers.Meta)) return;

            e.Handled = true;

            if (!editor.IsReadOnly) Duplicate(editor);
        }, RoutingStrategies.Tunnel);

    private static void Duplicate(TextEditor editor)
    {
        var document = editor.Document;

        if (editor.SelectionLength > 0)
        {
            var selected = editor.SelectedText;
            var after = editor.SelectionStart + editor.SelectionLength;

            document.Insert(after, selected);
            editor.Select(after, selected.Length);
            return;
        }

        var line = document.GetLineByOffset(editor.CaretOffset);
        var column = editor.CaretOffset - line.Offset;
        var newline = line.DelimiterLength > 0 ? document.GetText(line.EndOffset, line.DelimiterLength)
            : line.PreviousLine is { } before ? document.GetText(before.EndOffset, before.DelimiterLength)
            : "\n";

        var end = line.EndOffset;

        document.Insert(end, newline + document.GetText(line.Offset, line.Length));
        editor.CaretOffset = end + newline.Length + column;
    }
}
