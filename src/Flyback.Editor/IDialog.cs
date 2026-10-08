using Avalonia.Controls;

namespace Flyback.Editor.Controls;

/// <summary>
/// A panel over the window, to be dealt with before anything else happens.
/// </summary>
/// <remarks>
/// Built here rather than asked of the platform, because Avalonia has no message
/// box and one made by hand is the same palette, theme and font as the rest of the
/// program — which a native one is not, on any of the three platforms.
/// <para>
/// A panel and not a window: a second window is a second thing in the task
/// switcher and three different frames around the same three buttons, for a
/// question that belongs on this one. What makes it modal is what a modal window
/// is actually for — the shell behind is dimmed, cannot be clicked, and does not
/// hear the keyboard. See <see cref="ModalOverlay"/>.
/// </para>
/// </remarks>
internal interface IDialog
{
    /// <summary>
    /// Whether a dialog is over the window now. For whatever reaches the
    /// window without going through the pointer or the keyboard the sheet
    /// already stops — a file dropped from outside is the one there is.
    /// </summary>
    bool IsShowing { get; }
    
    Task Show(string title, Func<Action, Control> content, Control? header = null, bool fill = false) 
        => Show<object?>(title, c => content(() => c(null)), header, fill);
    
    Task Show(string title, Control content, Control? header = null, bool fill = false) 
        => Show<object?>(title, _ => content, header, fill);
    
    /// <summary>
    /// Puts <paramref name="content"/> over the window and waits for it to be
    /// answered — by <see cref="Close{TResult}"/>, or by the two ways out the
    /// frame provides: the cross on it, and Escape.
    /// </summary>
    /// <remarks>
    /// Dismissing it comes back as <c>default</c>: the answer nobody gave should
    /// be the one that loses nothing, and an enum whose first member is Cancel
    /// gets that from the language.
    /// </remarks>
    /// <param name="header">
    /// Shown between the title and the content, and kept there while the
    /// content scrolls — a filter that scrolled away with what it filters
    /// would be the wrong way round.
    /// </param>
    /// <param name="fill">
    /// Takes all the room it may rather than only what the content needs, for
    /// content whose size changes while it is up — a gallery being filtered
    /// would otherwise shrink and grow the frame around whatever is typed.
    /// </param>
    /// <param name="wide">
    /// Lets the frame grow past the usual width, and gives the content its height to
    /// scroll its own columns in rather than scrolling it whole.
    /// </param>
    /// <param name="top">
    /// Stands the frame at the top of the window rather than its middle, clear of an
    /// on-screen keyboard rising from the bottom.
    /// </param>
    Task<TResult> Show<TResult>(
        string title, 
        Func<Action<TResult>, Control> content,
        Control? header = null,
        bool fill = false,
        bool wide = false,
        bool top = false);
}