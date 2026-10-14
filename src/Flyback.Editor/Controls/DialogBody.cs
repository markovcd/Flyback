using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Flyback.Editor.Controls;

/// <summary>
/// A dialog's content over its row of buttons, with only the content scrolling.
/// </summary>
/// <remarks>
/// <see cref="ModalOverlay"/> scrolls whatever it is given whole, so a dialog that
/// answers through buttons hands them over this way to keep them in view in a
/// window too short for the rest.
/// </remarks>
internal sealed class DialogBody : DockPanel
{
    public DialogBody(Control content, Control buttons)
    {
        Content = content;
        Buttons = buttons;

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
}
