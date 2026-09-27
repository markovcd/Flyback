using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Flyback.App.Controls;

internal sealed class WindowDialog(WindowHolder holder) : IDialog
{
    public bool IsShowing => OverlayLayer.GetOverlayLayer(holder.Instance)?
        .Children.OfType<ModalOverlay>().Any() == true;
    
    public async Task<TResult> Show<TResult>(
            string title, 
            Func<Action<TResult>, Control> content, 
            Control? header = null, 
            bool fill = false)
    {
        // Avalonia's own layer for things drawn over a window — what a flyout
        // or a tooltip is put in. Using it rather than a panel of our own
        // means the shell's layout is not rearranged to make room for a
        // dialog it has nothing to do with. There is none before the window
        // has been shown, and nothing to show a dialog on either.
        if (OverlayLayer.GetOverlayLayer(holder.Instance) is not { } layer) return default!;

        // Where the keyboard was, so it can be put back. The overlay takes
        // the focus, and giving it to the canvas afterwards instead of to
        // whatever had it is its own small rudeness.
        var before = holder.Instance.FocusManager.GetFocusedElement();

        var overlay = new ModalOverlay(title, a => content(r => a(r)), header, fill);

        layer.Children.Add(overlay);
        overlay.Focus();

        try
        {
            return await overlay.Answered is TResult answer ? answer : default!;
        }
        finally
        {
            layer.Children.Remove(overlay);
            before?.Focus();
        }
    }
}