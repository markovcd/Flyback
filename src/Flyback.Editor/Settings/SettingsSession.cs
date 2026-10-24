using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.Editor.Composition;
using Flyback.Editor.Controls;
using Flyback.Editor.Notices;
using Flyback.Editor.Shell;
using Flyback.Editor.Statistics;
using Flyback.Ui.Controls;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Settings;

/// <summary>Shows the settings sections together and commits or restores their drafts.</summary>
internal sealed class SettingsSession(
    IEnumerable<ISettingsSection> sections,
    IDialog dialog,
    Usage usage,
    EditorHost host)
    : IReactTo<SettingsAsked>
{
    /// <summary>Whether the settings sheet is waiting for an answer.</summary>
    public bool IsShowing { get; private set; }

    /// <summary>A page has its own few settings instead, in <see cref="PageSettings"/>.</summary>
    public Task On(SettingsAsked notice) => host.InPage ? Task.CompletedTask : ShowAsync();

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

    /// <summary>How wide every section is: room for the widest option any list offers, beside the gutter.</summary>
    internal const double SectionWidth = 360;

    /// <summary>
    /// The tabs' size, list and section together: wide enough for the list beside
    /// a section with room for its scroll bar, and tall enough for every tab in one
    /// column and an assistant's usual form without a scroll bar. A window too short
    /// for that height gets the tabs as tall as it has room for, down to the list's.
    /// </summary>
    private const double Width = SectionWidth + 200;

    /// <inheritdoc cref="Width"/>
    private const double Height = 460;

    /// <summary>How tall one tab's row in the list is.</summary>
    private const double TabHeight = 40;

    /// <summary>The room above the list's first row.</summary>
    private const double ListTop = 6;

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
        // through; a section taller than that scrolls inside its own tab, and
        // the dialog scrolls only a window too short for the list. Save
        // sits under them all and never scrolls away, because it saves them all —
        // not only the tab showing.
        var tabs = new TabControl
        {
            Name = "settingsTabs",
            TabStripPlacement = Dock.Left,
            Padding = new Thickness(4, ListTop, 0, 0),
        };

        foreach (var section in sections) tabs.Items.Add(Tab(section));

        var save = new Button { Name = "save", Content = "Save", Width = 84, Classes = { "accent" } };

        // Cancel answers exactly what the cross and Escape answer, so all three
        // take the one way out rather than each undoing things itself.
        var cancel = new Button { Name = "cancel", Content = "Cancel", Width = 84 };

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

        return new DialogBody(
            new HeightUpTo(Height)
            {
                Child = tabs,
                Width = Width,
                MinHeight = tabs.ItemCount * TabHeight + ListTop,
                Margin = new Thickness(18, 4, 18, 0),
            },
            new StackPanel
            {
                Spacing = 12,
                Margin = new Thickness(18, 12, 18, 18),
                Children = { divider, buttons },
            },
            fits: true);
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
            Height = TabHeight,
            MinHeight = 0,
            Width = 120,
        };
    }
}
