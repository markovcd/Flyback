using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Flyback.App.Controls;

/// <summary>The box a name on the inspector's plate turns into when it is double-clicked.</summary>
internal static class NameBox
{
    /// <summary>What the pointer turns into over a name that double-clicks into a box.</summary>
    internal static readonly Cursor Renaming = new(StandardCursorType.Hand);

    /// <summary>
    /// Turns a title into a box to type another name into, and puts the title back
    /// when the box closes.
    /// </summary>
    /// <remarks>
    /// Written against what a name is rather than against what carries one, because
    /// two things carry one: a module and a group. Enter keeping, Escape
    /// discarding, losing the focus keeping, and only real edits reaching the
    /// history are the same for both.
    /// </remarks>
    /// <param name="title">The name as it is drawn, which the box takes the place of.</param>
    /// <param name="ink">What the name is written in, which the box is written in too.</param>
    /// <param name="held">The name it has, which is null on one nobody has named.</param>
    /// <param name="fallback">What it is called when it has no name of its own.</param>
    /// <param name="limit">The longest name the thing will keep.</param>
    /// <param name="rename">Takes what was typed, with whatever tidying the thing does to one.</param>
    /// <param name="current">The name as it stands, read again afterwards to see whether it moved.</param>
    /// <param name="rebuild">The title to put back.</param>
    /// <param name="changed">Tells the canvas the patch changed.</param>
    /// <param name="prose">Set as a line of body text that wraps, for a description, rather than as a title.</param>
    internal static void Open(
        Control title,
        IBrush ink,
        string? held,
        string fallback,
        int limit,
        Action<string?> rename,
        Func<string?> current,
        Func<Control> rebuild,
        Action changed,
        bool prose = false)
    {
        // The name stands on the plate rather than on the panel, so the box goes back
        // where the name was.
        if (title.Parent is not Panel host) return;

        var at = host.Children.IndexOf(title);
        if (at < 0) return;

        // Dressed as the name it replaces: same size, same weight, same ink, and
        // none of a box's own furniture. The name does not move when it is
        // double-clicked — what changes is that there is a caret in it.
        var box = new TextBox
        {
            // The name it has, not the one it shows. Opening this on something
            // nobody has renamed leaves an empty box, because empty is what it
            // means — and what it would go back to is the placeholder, which
            // reads as the thing you would get rather than as text to delete
            // before typing.
            Text = held ?? string.Empty,
            PlaceholderText = fallback,
            MaxLength = limit,
            FontSize = prose ? Text.Body : Text.Title,
            FontWeight = prose ? FontWeight.Normal : FontWeight.SemiBold,
            TextAlignment = prose ? TextAlignment.Left : TextAlignment.Right,
            TextWrapping = prose ? TextWrapping.Wrap : TextWrapping.NoWrap,
            Foreground = ink,
            CaretBrush = ink,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            MinHeight = 0,
            Classes = { ModulePlate.NameBoxClass },
        };

        // Enter takes the focus off the box as it closes it, which would bring
        // the focus handler round a second time. Every way out goes through the
        // one flag instead.
        var closed = false;

        box.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Enter: Close(keep: true); break;
                case Key.Escape: Close(keep: false); break;
                default: return;
            }

            e.Handled = true;
        };

        box.LostFocus += (_, _) => Close(keep: true);

        host.Children[at] = box;

        box.Focus();
        box.SelectAll();

        void Close(bool keep)
        {
            if (closed) return;
            closed = true;

            var before = current();
            if (keep) rename(box.Text);

            var where = host.Children.IndexOf(box);
            if (where >= 0) host.Children[where] = rebuild();

            // Only where it is actually a rename: the canvas draws its headers
            // from the same name and this is what redraws them, and a step in
            // the history for opening a box and closing it again would be one
            // press of undo that puts nothing back.
            if (current() != before) changed();
        }
    }
}
