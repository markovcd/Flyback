using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;

namespace Flyback.Editor.Controls;

/// <summary>
/// A dialog's content over its row of buttons, with only the content scrolling.
/// </summary>
/// <remarks>
/// <see cref="ModalOverlay"/> scrolls whatever it is given whole, so a dialog that
/// answers through buttons hands them over this way to keep them in view in a
/// window too short for the rest. Content passed with <c>fits</c> scrolls itself and
/// is given the room there is, so the body scrolls it only in less room than its
/// <see cref="Layoutable.MinHeight"/>.
/// </remarks>
internal sealed class DialogBody : DockPanel
{
    public DialogBody(Control content, Control buttons, bool fits = false)
    {
        Content = content;
        Buttons = buttons;
        Fits = fits;

        Scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = content,
        };

        SetDock(buttons, Dock.Bottom);
        Children.Add(buttons);
        Children.Add(Scroller);
    }

    public Control Content { get; }

    public Control Buttons { get; }

    public ScrollViewer Scroller { get; }

    /// <summary>Whether the content takes the room it is given and scrolls inside itself.</summary>
    public bool Fits { get; }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Fits)
        {
            Buttons.Measure(availableSize);

            var room = availableSize.Height - Buttons.DesiredSize.Height;
            var least = Content.MinHeight + Content.Margin.Top + Content.Margin.Bottom;

            Scroller.VerticalScrollBarVisibility = room < least ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        }

        return base.MeasureOverride(availableSize);
    }
}
