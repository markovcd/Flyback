using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.Editor.Controls;
using Flyback.Ui.Controls;

namespace Flyback.Editor.Inspect;

/// <summary>
/// Renames a block: a box where the name stands under a mouse, and under a finger a dialog at the
/// top of the window, which says what is being renamed and stays clear of the on-screen keyboard.
/// </summary>
internal sealed class Renamer(IDialog dialog, LastPress lastPress)
{
    /// <param name="title">The name being renamed, which a box takes the place of under a mouse.</param>
    /// <param name="what">What is being renamed, as the dialog says it.</param>
    /// <param name="held">The name it has of its own, or null.</param>
    /// <param name="fallback">What it is called without one.</param>
    /// <param name="rebuild">The name drawn again, once renamed.</param>
    public void Open(
        Control title,
        IBrush ink,
        string what,
        string? held,
        string fallback,
        int limit,
        Action<string?> rename,
        Func<string?> current,
        Func<Control> rebuild,
        Action changed)
    {
        if (!lastPress.ByFinger)
        {
            NameBox.Open(title, ink, held, fallback, limit, rename, current, rebuild, changed);
            return;
        }

        _ = Ask(title, what, held, fallback, limit, rename, current, rebuild, changed);
    }

    private async Task Ask(
        Control title,
        string what,
        string? held,
        string fallback,
        int limit,
        Action<string?> rename,
        Func<string?> current,
        Func<Control> rebuild,
        Action changed)
    {
        // Null is the question put away; an empty name is a name, the block's own back.
        var typed = await dialog.Show<string?>("Rename", answer => Form(what, held, fallback, limit, answer), top: true);
        if (typed is null) return;

        var before = current();
        rename(typed);

        if (title.Parent is Panel host && host.Children.IndexOf(title) is >= 0 and var at) host.Children[at] = rebuild();

        if (current() != before) changed();
    }

    private static Control Form(string what, string? held, string fallback, int limit, Action<string?> answer)
    {
        var box = new TextBox
        {
            Name = "rename-box",
            Text = held ?? string.Empty,
            PlaceholderText = fallback,
            MaxLength = limit,
            FontSize = Text.Heading,
        };

        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;

            e.Handled = true;
            answer(box.Text ?? string.Empty);
        };

        box.AttachedToVisualTree += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };

        var cancel = new Button { Name = "rename-cancel", Content = "Cancel", MinWidth = 88, Height = 44 };
        cancel.Click += (_, _) => answer(null);

        var keep = new Button { Name = "rename-keep", Content = "Rename", MinWidth = 88, Height = 44, Classes = { "accent" } };
        keep.Click += (_, _) => answer(box.Text ?? string.Empty);

        return new StackPanel
        {
            Spacing = 10,
            MinWidth = 280,
            Children =
            {
                new TextBlock { Text = what, FontSize = Text.Body, Foreground = Text.Muted, TextWrapping = TextWrapping.Wrap },
                box,
                new TextBlock
                {
                    Text = $"Empty it to go back to “{fallback}”.",
                    FontSize = Text.Small,
                    Foreground = Text.Muted,
                    TextWrapping = TextWrapping.Wrap,
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { cancel, keep },
                },
            },
        };
    }
}
