using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit;
using Flyback.Core.Graph;
using Flyback.Engine.Language;
using Flyback.Ui.Controls;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Hubs;

/// <summary>What the word under the pointer is, in a tooltip: a module, a socket, a name the text bound or one the language gives.</summary>
internal sealed class SourceHover
{
    private readonly TextEditor editor;

    public SourceHover(TextEditor editor)
    {
        this.editor = editor;

        var view = editor.TextArea.TextView;
        view.PointerHover += (_, e) =>
        {
            var at = editor.GetPositionFromPoint(e.GetPosition(editor));

            if (at is { } position) Show(position.Line, position.Column);
            else Hide();
        };
        view.PointerHoverStopped += (_, _) => Hide();
    }

    /// <summary>What the tooltip is saying, or null while it is shut.</summary>
    public Hover? Showing { get; private set; }

    /// <summary>Says what the word at a place is, as resting the pointer on it does.</summary>
    public void Show(int line, int column)
    {
        var view = editor.TextArea.TextView;

        if (Hovers.At(editor.Text, line, column, NodeCatalog.Current) is not { } hover)
        {
            Hide();
            return;
        }

        Showing = hover;

        var said = new StackPanel { Spacing = 4, MaxWidth = 480 };
        said.Children.Add(new TextBlock { Text = hover.Title, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });

        if (hover.Body.Length > 0)
            said.Children.Add(new TextBlock
            {
                Text = hover.Body,
                FontSize = Text.Small,
                Foreground = new SolidColorBrush(Colors.Muted),
                TextWrapping = TextWrapping.Wrap,
            });

        ToolTip.SetTip(view, said);
        ToolTip.SetIsOpen(view, true);
    }

    private void Hide()
    {
        Showing = null;

        var view = editor.TextArea.TextView;
        ToolTip.SetIsOpen(view, false);
        ToolTip.SetTip(view, null);
    }
}
