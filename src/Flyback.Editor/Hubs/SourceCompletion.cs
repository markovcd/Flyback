using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using Flyback.Core.Graph;
using Flyback.Engine.Language;

namespace Flyback.Editor.Hubs;

/// <summary>
/// The list of what may be typed at the caret, popped up under it: the sockets of
/// the call it is in, the outputs after a dot, the names in sight and the catalog.
/// </summary>
/// <remarks>
/// It opens at the start of a word, after a dot or a bracket, and on Ctrl+Space,
/// and only where <see cref="Completions"/> has something to offer, so typing a new
/// name or a number never brings it up.
/// </remarks>
internal sealed class SourceCompletion
{
    private readonly TextEditor editor;
    private CompletionWindow? window;

    public SourceCompletion(TextEditor editor)
    {
        this.editor = editor;

        editor.TextArea.TextEntered += Entered;
        editor.AddHandler(InputElement.KeyDownEvent, Asked, RoutingStrategies.Tunnel);
    }

    /// <summary>The words the list is showing, or null while it is shut.</summary>
    /// <remarks>Only tests read it: the seam that says what a popup no test can see is offering.</remarks>
    public IReadOnlyList<Completion>? Showing =>
        window?.CompletionList.CompletionData.OfType<SourceCompletionItem>().Select(i => i.Offered).ToArray();

    private void Entered(object? sender, TextInputEventArgs e)
    {
        if (e.Text is not [var typed]) return;

        var word = char.IsAsciiLetterOrDigit(typed) || typed == '_';

        if (window is not null)
        {
            if (!word) window.Close();
            else return;
        }

        if (typed is '.' or '(' || (word && !char.IsAsciiDigit(typed) && StartsWord())) Open();
    }

    /// <summary>Whether the character just typed is the first of its word.</summary>
    private bool StartsWord()
    {
        var before = editor.CaretOffset - 2;

        return before < 0 || editor.Document.GetCharAt(before) is var c && !(char.IsAsciiLetterOrDigit(c) || c == '_');
    }

    private void Asked(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) == 0) return;

        e.Handled = true;
        window?.Close();
        Open();
    }

    private void Open()
    {
        var caret = editor.TextArea.Caret;
        var list = Completions.At(editor.Text, caret.Line, caret.Column, NodeCatalog.Current);

        if (list.Items.Count == 0) return;

        var opened = new CompletionWindow(editor.TextArea)
        {
            StartOffset = editor.Document.GetOffset(list.Line, list.Column),
        };

        for (var i = 0; i < list.Items.Count; i++)
            opened.CompletionList.CompletionData.Add(new SourceCompletionItem(list.Items[i], i));

        opened.Closed += (_, _) =>
        {
            if (window == opened) window = null;
        };

        window = opened;
        opened.Show();

        if (list.Typed.Length > 0) opened.CompletionList.SelectItem(list.Typed);
    }
}
