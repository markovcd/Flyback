using System.Collections.Immutable;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.Editor.Controls;
using Flyback.Ui.Controls;
using Flyback.Editor.Notices;
using Flyback.Editor.Statistics;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Settings;

/// <summary>Shows the settings sections together and commits or restores their drafts.</summary>
internal sealed class SettingsSession(
    IEnumerable<ISettingsSection> sections,
    IDialog dialog,
    Usage usage)
    : IReactTo<SettingsAsked>
{
    /// <summary>Whether the settings sheet is waiting for an answer.</summary>
    public bool IsShowing { get; private set; }
    
    public Task On(SettingsAsked notice) => ShowAsync();

    /// <summary>Shows all sections, then saves their drafts or restores the last saved values.</summary>
    private async Task ShowAsync()
    {
        foreach (var section in sections) section.Opening();

        IsShowing = true;
        usage.Count(Used.Settings);

        bool saved;

        try
        {
            saved = await dialog.Show<bool>("Settings", Content);
        }
        finally
        {
            IsShowing = false;
        }

        if (saved)
        {
            foreach (var section in sections) section.Save();
            return;
        }

        foreach (var section in sections) section.Show();
    }
    
    /// <summary>
    /// The tabs' size, list and section together: wide enough for the list beside
    /// a 280-pixel section with room for its scroll bar, and tall enough for every
    /// tab in one column and an assistant's usual form without a scroll bar.
    /// </summary>
    private const double Width = 480;

    /// <inheritdoc cref="Width"/>
    private const double Height = 460;

    private Control Content(Action<bool> answer)
    {
        // The window around the sections is built fresh, so each has to be taken
        // back from the last one first.
        foreach (var section in sections)
            if (section.View.Parent is ContentControl lender) lender.Content = null;

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

        foreach (var section in sections) tabs.Items.Add(Tab(section));

        var save = new Button { Content = "Save", Width = 84 };

        // Cancel answers exactly what the cross and Escape answer, so all three
        // take the one way out rather than each undoing things itself.
        var cancel = new Button { Content = "Cancel", Width = 84 };

        save.Click += (_, _) => answer(true);
        cancel.Click += (_, _) => answer(false);

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

        return new StackPanel
        {
            Spacing = 12,
            MinWidth = Width,
            Margin = new Thickness(18, 4, 18, 18),
            Children = { tabs, divider, buttons },
        };
    }

    /// <summary>
    /// One section as a tab. The header is a text block sized like the rest of the
    /// window, because the theme's own tab header is set at page-title size.
    /// </summary>
    /// <remarks>
    /// The section scrolls in its own viewer, since the tabs are a fixed height
    /// and an assistant's form is as long as its provider declares it to be.
    /// </remarks>
    private static TabItem Tab(ISettingsSection section)
    {
        section.View.HorizontalAlignment = HorizontalAlignment.Left;

        return new TabItem
        {
            Header = new TextBlock { Text = section.Name, FontSize = Text.Emphasis, FontWeight = FontWeight.SemiBold },
            Content = new Border
            {
                Padding = new Thickness(16),
                Child = new ScrollViewer
                {
                    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                    Content = section.View,
                },
            },
            Padding = new Thickness(4, 6, 12, 6),
            Height = 46,
            Width = 120,
        };
    }
}
