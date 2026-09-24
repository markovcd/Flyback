using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.App.Controls;
using Colors = Flyback.App.Controls.Colors;

namespace Flyback.App;

/// <summary>
/// The settings window: a tab a section, and one Save for all of them (ADR-0082).
/// </summary>
/// <remarks>
/// Every section is a set of controls lent to the window rather than built for it,
/// so what they were last set to is still on them the next time it opens. What
/// Save or Cancel then does to them is the caller's.
/// </remarks>
internal static class SettingsDialog
{
    /// <summary>
    /// The tabs' size, list and section together: wide enough for the list beside
    /// a 280-pixel section with room for its scroll bar, and tall enough for every
    /// tab in one column and an assistant's usual form without a scroll bar.
    /// </summary>
    private const double Width = 480;

    /// <inheritdoc cref="Width"/>
    private const double Height = 460;

    /// <summary>Shows <paramref name="sections"/> and answers whether Save closed it.</summary>
    /// <remarks>
    /// Cancel, the cross and Escape all answer false — see Dialog.ShowDialog —
    /// which is every way out that is not Save.
    /// </remarks>
    public static async Task<bool> ShowAsync(Window owner, IReadOnlyList<(string Name, Control Section)> sections)
    {
        // The window around the sections is built fresh, so each has to be taken
        // back from the last one first.
        foreach (var (_, section) in sections)
            if (section.Parent is ContentControl lender) lender.Content = null;

        // Tabs rather than one long column, so moving between sections is a
        // click rather than a scroll, listed down the left so a section added
        // later is one more row rather than a strip running out of width. A
        // fixed size, so the window does not jump as the sections are flicked
        // through; a section taller than that scrolls inside its own tab. Save
        // sits under them all, because it saves them all — not only the tab
        // showing.
        var tabs = new TabControl
        {
            Name = "settingsTabs",
            TabStripPlacement = Dock.Left,
            Width = Width,
            Height = Height,
            Padding = new Thickness(4, 6, 0, 0),
        };

        foreach (var (name, section) in sections) tabs.Items.Add(Tab(name, section));

        var save = new Button { Content = "Save", Width = 84 };

        // Cancel answers exactly what the cross and Escape answer, so all three
        // take the one way out rather than each undoing things itself.
        var cancel = new Button { Content = "Cancel", Width = 84 };

        save.Click += (_, _) => Dialog.Close(save, true);
        cancel.Click += (_, _) => Dialog.Close(cancel, false);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { save, cancel },
        };

        // A line rather than a box around the tabs, so it reads as one sheet
        // that ends before the buttons rather than a bordered pane sitting on
        // another.
        var divider = new Border { Height = 1, Background = new SolidColorBrush(Colors.Separator) };

        var content = new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(18, 4, 18, 18),
            Children = { tabs, divider, buttons },
        };

        return await owner.ShowDialog<bool>("Settings", content);
    }

    /// <summary>
    /// One section as a tab. The header is a text block sized like the rest of the
    /// window, because the theme's own tab header is set at page-title size.
    /// </summary>
    /// <remarks>
    /// The section scrolls in its own viewer, since the tabs are a fixed height
    /// and an assistant's form is as long as its provider declares it to be.
    /// </remarks>
    private static TabItem Tab(string name, Control section)
    {
        section.HorizontalAlignment = HorizontalAlignment.Left;

        return new TabItem
        {
            Header = new TextBlock { Text = name, FontSize = Text.Emphasis, FontWeight = FontWeight.SemiBold },
            Content = new Border
            {
                Padding = new Thickness(16),
                Child = new ScrollViewer
                {
                    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                    Content = section,
                },
            },
            Padding = new Thickness(4, 6, 12, 6),
            Height = 46,
            Width = 120,
        };
    }
}
