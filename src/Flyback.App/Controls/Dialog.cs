using Avalonia.Controls;
using Avalonia.Media;

namespace Flyback.App.Controls;

/// <summary>
/// A window over the main one, to be dealt with before anything else happens.
/// </summary>
/// <remarks>
/// Built here rather than asked of the platform, because Avalonia has no message
/// box and one made by hand is the same palette, the same theme and the same
/// font as everything else in the program — which a native one is not, on any of
/// the three platforms this ships to.
/// <para>
/// Deliberately plain: no buttons, no title text, no icon. What a dialog asks
/// and what its answers are belong to whatever put it up, and the two here have
/// nothing in common but their frame.
/// </para>
/// </remarks>
internal static class Dialog
{
    extension(Window owner)
    {
        public Task<TResult> ShowDialog<TResult>(string title, Control content)
        {
            return Around(title, content).ShowDialog<TResult>(owner);
        }

        public Task ShowDialog(string title, Control content)
        {
            return Around(title, content).ShowDialog(owner);
        }
    }
    
    /// <summary>
    /// A modal window around some content, to be shown with
    /// <see cref="Window.ShowDialog(Window)"/>.
    /// </summary>
    /// <remarks>
    /// It sizes to what is in it and cannot be resized: everything shown this
    /// way is a handful of fields or a sentence and a row of buttons, and a
    /// corner to drag would only ever make one of those look wrong. Closing it
    /// by its own frame is allowed, and what that means is the caller's to
    /// decide — the answer nobody gave should be the one that loses nothing.
    /// </remarks>
    private static Window Around(string title, Control content) => new()
    {
        Title = title,
        Content = content,
        SizeToContent = SizeToContent.WidthAndHeight,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        CanResize = false,
        ShowInTaskbar = false,
        Background = new SolidColorBrush(Colors.Panel),
    };
}
