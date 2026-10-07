using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.Editor.Bars;
using Flyback.Editor.Canvas;
using Flyback.Editor.Notices;
using Flyback.Ui.Controls;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Settings;

/// <summary>
/// A page's settings (ADR-0182): a small panel off the toolbar's gear, each switch
/// taking effect and kept in the browser as it is ticked, with no Save.
/// </summary>
internal sealed class PageSettings : IReactTo<SettingsAsked>, IReactTo<Touched>
{
    private const double PanelWidth = 320;

    private readonly EditorHost host;
    private readonly Toolbar toolbar;
    private readonly CanvasSection canvas;

    private readonly CheckBox dragToPan = Switch("pageDragToPan", "Drag empty canvas to pan");
    private readonly CheckBox compactModules = Switch("pageCompactModules", "Compact modules");

    /// <summary>Drag to pan and its note, which a finger has no use for.</summary>
    private readonly Border panRow;

    public PageSettings(EditorHost host, Toolbar toolbar, CanvasSection canvas)
    {
        this.host = host;
        this.toolbar = toolbar;
        this.canvas = canvas;

        var close = ToolbarButtons.Drawn("page-settings-close", Glyphs.Cross(), "Close the settings.");
        close.Click += (_, _) => Flyout.Hide();

        var title = new TextBlock
        {
            Text = "Settings",
            FontSize = Text.Heading,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var header = new DockPanel { Margin = new Thickness(6, 0, 0, 4) };
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        header.Children.Add(title);

        panRow = new Border
        {
            Name = "pageDragToPanRow",
            Background = new SolidColorBrush(Colors.Window),
            BorderBrush = new SolidColorBrush(Colors.Edge),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(8),
            Child = Row(dragToPan,
                "For a mouse or trackpad without a middle button. Left-drag moves the view; "
                + "right-drag selects, right-click adds a module."),
        };

        var compactRow = Row(compactModules, "Puts each input beside an output on one row.");
        compactRow.Margin = new Thickness(8, 4, 8, 4);

        Flyout.Content = new StackPanel
        {
            Name = "pageSettings",
            Width = PanelWidth,
            Spacing = 6,
            Children = { header, panRow, compactRow },
        };

        // Opening sets the boxes to what is saved, so the handlers below hear only a hand.
        Flyout.Opening += (_, _) =>
        {
            dragToPan.IsChecked = canvas.DragToPan;
            compactModules.IsChecked = canvas.CompactModules;
        };

        dragToPan.IsCheckedChanged += (_, _) => Change(settings => settings.DragToPan = dragToPan.IsChecked == true);
        compactModules.IsCheckedChanged += (_, _) => Change(settings => settings.CompactModules = compactModules.IsChecked == true);
    }

    /// <summary>The panel, open or shut; for a test.</summary>
    internal Flyout Flyout { get; } = new() { Placement = PlacementMode.BottomEdgeAlignedRight };

    public Task On(SettingsAsked notice)
    {
        if (!host.InPage) return Task.CompletedTask;

        // From the gear, or from the menu it has folded into.
        Flyout.ShowAt(toolbar.Settings.IsVisible ? toolbar.Settings : toolbar.Overflow.More);

        return Task.CompletedTask;
    }

    public Task On(Touched notice)
    {
        panRow.IsVisible = false;
        return Task.CompletedTask;
    }

    private void Change(Action<CanvasSettings> change)
    {
        if (Flyout.IsOpen) canvas.Change(change);
    }

    private static CheckBox Switch(string name, string label) => new()
    {
        Name = name,
        Content = label,
        FontSize = Text.Emphasis,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static StackPanel Row(CheckBox box, string note) => new()
    {
        Spacing = 2,
        Children =
        {
            box,
            new TextBlock
            {
                Text = note,
                FontSize = Text.Small,
                Foreground = Text.Muted,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(28, 0, 0, 0),
            },
        },
    };
}
