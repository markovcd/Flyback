using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Flyback.App.Controls;

namespace Flyback.App.Bars;

/// <summary>
/// Keeps the toolbar to one row: what does not fit folds into a menu at its end, the
/// least reached for first, and comes back as the window widens.
/// </summary>
/// <remarks>
/// A folded button is hidden, not moved, and its menu item presses it, so the button
/// stays the one place its tip, its state and its handler live.
/// </remarks>
internal sealed class ToolbarOverflow
{
    public const string MoreTip = "The toolbar's buttons that do not fit beside the rest.";

    private readonly Panel bar;
    private readonly (Control Control, string Label)[] order;
    private readonly (Panel Group, Control Separator)[] groups;
    private readonly HashSet<Control> folded = [];
    private readonly MenuFlyout menu = new() { Placement = PlacementMode.BottomEdgeAlignedRight };

    /// <param name="bar">The row the buttons stand in, laid out as wide as they ask.</param>
    /// <param name="order">The buttons that may fold, the first to go first, each with what its menu item says.</param>
    /// <param name="groups">Groups put away whole, with the separator before them, once all they hold is folded.</param>
    public ToolbarOverflow(Panel bar, (Control Control, string Label)[] order, (Panel Group, Control Separator)[] groups)
    {
        this.bar = bar;

        // A page leaves some of them off the bar altogether.
        var standing = bar.GetLogicalDescendants().ToHashSet();
        this.order = order.Where(entry => standing.Contains(entry.Control)).ToArray();
        this.groups = groups;

        More = ToolbarButtons.Drawn("toolbar-more", Glyphs.Dots(), MoreTip);
        More.IsVisible = false;
        More.Margin = new Thickness(4, 0, 12, 0);
        More.VerticalAlignment = VerticalAlignment.Center;
        More.Flyout = menu;
        AutomationProperties.SetName(More, "More");

        menu.Opening += (_, _) => menu.ItemsSource = Items();

        foreach (var (control, label) in this.order) AutomationProperties.SetName(control, label);
    }

    /// <summary>The button the menu hangs from, shown only while something is folded.</summary>
    public Button More { get; }

    /// <summary>The folded buttons, in the toolbar's order.</summary>
    public IEnumerable<Control> Folded => bar.GetLogicalDescendants().OfType<Control>().Where(folded.Contains);

    /// <summary>Folds what does not fit in <paramref name="width"/>, after bringing back all that was folded.</summary>
    public void Fit(double width)
    {
        if (width <= 0) return;

        foreach (var control in folded) Show(control, true);
        folded.Clear();
        Tidy();

        foreach (var (control, _) in order)
        {
            if (Wanted() <= width) break;
            if (!control.IsVisible) continue;

            Show(control, false);
            folded.Add(control);
            Tidy();
        }
    }

    /// <summary>
    /// Shows or hides a button, and has every panel between it and the row measured
    /// again, which a change of visibility alone does not reach before the next layout.
    /// </summary>
    private void Show(Control control, bool shown)
    {
        control.IsVisible = shown;

        for (var at = control.Parent as Layoutable; at is not null && at != bar.Parent; at = at.Parent as Layoutable)
            at.InvalidateMeasure();
    }

    /// <summary>How wide the row asks to be as it stands.</summary>
    private double Wanted()
    {
        bar.Measure(Size.Infinity);
        return bar.DesiredSize.Width;
    }

    /// <summary>Shows the menu's button while it holds anything, and puts away a group with nothing left in it.</summary>
    private void Tidy()
    {
        More.IsVisible = folded.Count > 0;

        foreach (var (group, separator) in groups)
        {
            var any = group.Children.Any(child => child.IsVisible);

            separator.IsVisible = any;
            Show(group, any);
        }
    }

    private List<MenuItem> Items() =>
        Folded.Select(control =>
        {
            var label = order.First(entry => entry.Control == control).Label;
            var item = new MenuItem { Header = label, IsEnabled = control.IsEnabled };

            if (control is ToggleButton toggle)
            {
                item.ToggleType = MenuItemToggleType.CheckBox;
                item.IsChecked = toggle.IsChecked == true;
                item.Click += (_, _) => toggle.IsChecked = toggle.IsChecked != true;
            }
            else
            {
                item.Click += (_, _) => control.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }

            return item;
        }).ToList();
}
