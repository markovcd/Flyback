using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Media;
using AvaloniaEdit;
using Flyback.Core.Graph;
using Flyback.Engine.Language;
using Flyback.Ui.Controls;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Hubs;

/// <summary>
/// The sockets of the call the caret is in, on one line above it: the one being
/// filled in bold with what it is for beneath, and the one the pipe fills marked.
/// </summary>
/// <remarks>Stood aside while the completion list is open, which sits where it would.</remarks>
internal sealed class SourceSignature
{
    private readonly TextEditor editor;
    private readonly SourceCompletion completing;

    private readonly TextBlock call = new() { FontSize = Text.Emphasis, Foreground = new SolidColorBrush(Colors.Label) };

    private readonly TextBlock help = new()
    {
        FontSize = Text.Small,
        Foreground = new SolidColorBrush(Colors.Muted),
        TextWrapping = TextWrapping.Wrap,
        MaxWidth = 480,
    };

    public SourceSignature(TextEditor editor, SourceCompletion completing)
    {
        this.editor = editor;
        this.completing = completing;

        Popup = new Popup
        {
            Name = "signature",
            PlacementTarget = editor.TextArea.TextView,
            Placement = PlacementMode.AnchorAndGravity,
            PlacementAnchor = PopupAnchor.TopLeft,
            PlacementGravity = PopupGravity.TopRight,
            IsLightDismissEnabled = false,
            Child = new Border
            {
                Background = new SolidColorBrush(Colors.Toolbar),
                BorderBrush = new SolidColorBrush(Colors.Separator),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 4),
                Child = new StackPanel { Spacing = 2, Children = { call, help } },
            },
        };

        editor.TextArea.Caret.PositionChanged += (_, _) => Refresh();
        editor.TextArea.LostFocus += (_, _) => Hide();
        completing.Opened += (_, _) => Hide();
        completing.Closed += (_, _) => Refresh();
    }

    /// <summary>The popup, which needs a place in the tree to open from.</summary>
    public Popup Popup { get; }

    /// <summary>The call being shown, or null while nothing is.</summary>
    public Signature? Showing { get; private set; }

    private void Refresh()
    {
        if (!editor.TextArea.IsKeyboardFocusWithin || completing.Showing is not null)
        {
            Hide();
            return;
        }

        var caret = editor.TextArea.Caret;

        if (Signatures.At(editor.Text, caret.Line, caret.Column, NodeCatalog.Current) is not { } signature)
        {
            Hide();
            return;
        }

        Showing = signature;
        Write(signature);

        var view = editor.TextArea.TextView;
        Popup.PlacementRect = caret.CalculateCaretRectangle().Translate(-view.ScrollOffset);
        Popup.IsOpen = true;
    }

    private void Hide()
    {
        Showing = null;
        Popup.IsOpen = false;
    }

    private void Write(Signature signature)
    {
        var inlines = new InlineCollection { new Run(signature.Callee + "(") };

        for (var i = 0; i < signature.Sockets.Count; i++)
        {
            if (i > 0) inlines.Add(new Run(", ") { Foreground = new SolidColorBrush(Colors.Inactive) });

            var socket = new Run(signature.Sockets[i].Name);

            if (i == signature.Active) socket.FontWeight = FontWeight.Bold;
            else socket.Foreground = new SolidColorBrush(Colors.Value);

            if (i == signature.Piped) inlines.Add(new Run("|> ") { Foreground = new SolidColorBrush(Colors.Attention) });

            inlines.Add(socket);
        }

        inlines.Add(new Run(")"));
        call.Inlines = inlines;

        var active = signature.Active >= 0 ? signature.Sockets[signature.Active] : null;
        var said = active is null
            ? signature.Title
            : string.Join(" · ", new[] { active.Name, active.Reads, active.Help }.Where(s => s.Length > 0));

        help.Text = said;
    }
}
