using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Flyback.Ui.Controls;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Settings;

/// <summary>
/// The settings tabs with their list down the left where there is room for both, and
/// where there is not, as on a phone, a list of the tabs to tap into, each tab then
/// shown alone and full width under a way back to the list.
/// </summary>
internal sealed class SettingsSheet : Grid
{
    /// <summary>How tall a row of the list is on a phone.</summary>
    private const double RowHeight = 40;

    private readonly TabControl tabs;
    private readonly double wide;
    private readonly IReadOnlyList<Control> views;
    private readonly ScrollViewer list;
    private readonly DockPanel back;
    private readonly TextBlock title = new() { FontSize = Text.Emphasis, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>Whether the sheet is too narrow for the list beside a tab; null until first measured.</summary>
    private bool? narrow;

    /// <summary>Whether a narrow sheet shows one tab rather than the list of them.</summary>
    private bool paged;

    /// <param name="tabs">The tabs, each section's preceded by its group's name.</param>
    /// <param name="wide">The width the list beside a tab needs; less than it is a phone's sheet.</param>
    /// <param name="views">The sections' controls, which a phone's sheet lets run to its width.</param>
    public SettingsSheet(TabControl tabs, double wide, IReadOnlyList<Control> views)
    {
        this.tabs = tabs;
        this.wide = wide;
        this.views = views;

        RowDefinitions = new RowDefinitions("Auto,*");

        var rows = new StackPanel();

        foreach (var item in tabs.Items.Cast<TabItem>())
        {
            if (!item.IsEnabled)
            {
                if (item.Header is Border { Child: TextBlock group })
                    rows.Children.Add(new TextBlock
                    {
                        Text = group.Text,
                        FontSize = Text.Caption,
                        Foreground = Text.Muted,
                        Margin = new Thickness(4, 12, 0, 4),
                    });
                else
                    rows.Children.Add(new Border { Height = 1, Margin = new Thickness(4, 10, 4, 6), Background = new ImmutableSolidColorBrush(Colors.Separator) });

                continue;
            }

            var name = (item.Header as TextBlock)?.Text ?? "";
            var row = new Button
            {
                Name = "settingsRow",
                Height = RowHeight,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0, 0, 0, 1),
                BorderBrush = new ImmutableSolidColorBrush(Colors.Panel),
                Content = new DockPanel
                {
                    Children =
                    {
                        Right(new TextBlock { Text = "›", FontSize = Text.Heading, Foreground = Text.Muted }),
                        new TextBlock { Text = name, FontSize = Text.Emphasis, VerticalAlignment = VerticalAlignment.Center },
                    },
                },
            };

            row.Click += (_, _) =>
            {
                tabs.SelectedItem = item;
                Page(true);
            };

            rows.Children.Add(row);
        }

        list = new ScrollViewer { Name = "settingsList", Content = rows, IsVisible = false };

        var backButton = new Button
        {
            Name = "settingsBack",
            Content = "‹ Settings",
            Background = Brushes.Transparent,
            Foreground = new ImmutableSolidColorBrush(Colors.Attention),
            Padding = new Thickness(4, 2, 10, 2),
        };

        backButton.Click += (_, _) => Page(false);

        back = new DockPanel { IsVisible = false, Margin = new Thickness(0, 0, 0, 6), Children = { Left(backButton), title } };

        // A tab picked from outside the list, by a script or a test, is shown as a tap shows it.
        tabs.SelectionChanged += (_, _) =>
        {
            if (narrow == true && !paged) Page(true);
        };

        SetRow(back, 0);
        SetRow(tabs, 1);
        SetRow(list, 1);

        Children.Add(back);
        Children.Add(tabs);
        Children.Add(list);
    }

    /// <summary>Whether the sheet is a phone's; for a test.</summary>
    internal bool Narrow => narrow == true;

    protected override Size MeasureOverride(Size availableSize)
    {
        var fits = availableSize.Width >= wide;

        if (narrow != !fits)
        {
            narrow = !fits;
            paged = false;
            Lay();
        }

        var size = base.MeasureOverride(availableSize.WithWidth(Math.Min(availableSize.Width, wide)));

        return fits ? size.WithWidth(wide) : size;
    }

    private void Page(bool on)
    {
        paged = on;
        Lay();
    }

    /// <summary>Shows the list beside the tab, the list alone, or one tab alone, as the width and the last tap say.</summary>
    private void Lay()
    {
        var phone = narrow == true;

        // A tab's header hidden leaves its page showing, so a phone's page is the tab
        // control with its list put away.
        foreach (var item in tabs.Items.Cast<TabItem>()) item.IsVisible = !phone;

        foreach (var view in views) view.Width = phone ? double.NaN : SettingsSession.SectionWidth;

        tabs.Padding = phone ? new Thickness(0) : new Thickness(4, SettingsSession.ListTop, 0, 0);
        tabs.IsVisible = !phone || paged;
        list.IsVisible = phone && !paged;
        back.IsVisible = phone && paged;
        title.Text = (tabs.SelectedItem as TabItem)?.Header is TextBlock { Text: { } name } ? name : "";
    }

    private static Control Right(Control control)
    {
        DockPanel.SetDock(control, Dock.Right);
        return control;
    }

    private static Control Left(Control control)
    {
        DockPanel.SetDock(control, Dock.Left);
        return control;
    }
}
