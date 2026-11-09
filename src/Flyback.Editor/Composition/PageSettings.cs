using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.Editor.Bars;
using Flyback.Editor.Canvas;
using Flyback.Editor.Controls;
using Flyback.Editor.Notices;
using Flyback.Editor.Settings;
using Flyback.Editor.Shell;
using Flyback.Editor.Windows;
using Flyback.Engine.Render;
using Flyback.Host;
using Flyback.Ui;
using Flyback.Ui.Audio;
using Flyback.Ui.Controls;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor.Composition;

/// <summary>
/// A page's settings (ADR-0182): a small panel off the toolbar's gear, each switch
/// taking effect and kept in the browser as it is ticked, with no Save.
/// </summary>
internal sealed class PageSettings : IReactTo<SettingsAsked>, IReactTo<Touched>
{
    private const double PanelWidth = 340;

    private readonly EditorHost host;
    private readonly Toolbar toolbar;
    private readonly CanvasSection canvas;

    private readonly CheckBox dragToPan = Switch("pageDragToPan", "Drag empty canvas to pan");
    private readonly CheckBox compactModules = Switch("pageCompactModules", "Compact modules");
    private readonly CheckBox endless = Switch("pageEndless", "Endless");

    /// <summary>Which edge of a full-screen picture the transport waits at, and so which the knobs do.</summary>
    private readonly ComboBox transportEdge = new Picker
    {
        Name = "pageTransportEdge",
        ItemsSource = new[] { "Top, knobs below", "Bottom, knobs on top" },
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>The oversampling the sound starts at, one row a factor of <see cref="AudioRenderer.Oversamples"/>.</summary>
    private readonly ComboBox oversample = new Picker
    {
        Name = "pageOversample",
        ItemsSource = AudioRenderer.Oversamples.Select(OversamplingText.Choice).ToList(),
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    private readonly ComboBox scroll = new Picker
    {
        Name = "pageScroll",
        ItemsSource = CanvasSection.ScrollChoices,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>Drag to pan and the scroll, which a finger has no use for.</summary>
    private readonly Border panRow;

    public PageSettings(EditorHost host, Toolbar toolbar, CanvasSection canvas, LivePatchingRows live, FullScreenSection fullScreen, IAudioEngine audio, OutputSettingRepository settings, IBrowserStore browser)
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
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    Row(dragToPan,
                        "For a mouse or trackpad without a middle button. Left-drag moves the view; "
                        + "right-drag selects, right-click adds a module."),
                    InspectorRows.Field("Scroll", scroll),
                },
            },
        };

        ToolTip.SetTip(scroll, CanvasSection.ScrollTip);

        var compactRow = Row(compactModules, "Puts each input beside an output on one row.");
        compactRow.Margin = new Thickness(8, 4, 8, 4);

        var endlessRow = Row(endless, "A patch with no length plays on until paused, with no seek bar.");
        endlessRow.Margin = new Thickness(8, 4, 8, 4);

        var oversampleRow = Row(oversample, "Oversampling", "Lowered a step by itself whenever the sound keeps falling behind.");
        var transportRow = Row(transportEdge, "Transport at", "Over a full-screen picture; the knobs take the other edge.");

        live.View.Margin = new Thickness(8, 0, 8, 4);

        // Taking effect as it is changed and kept in the browser, as the switches are.
        live.Changed += () =>
        {
            if (!Flyout.IsOpen) return;

            live.Read(settings.Current);
            live.Apply(settings.Current);
            browser.Write(LivePatchingLook.Section, settings.Current.Live.Json());
        };

        Flyout.Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Name = "pageSettings",
                Width = PanelWidth,
                Spacing = 6,
                Children =
                {
                    header,
                    Heading("Canvas"),
                    panRow,
                    compactRow,
                    Heading("Playing"),
                    endlessRow,
                    Heading("Sound"),
                    oversampleRow,
                    Heading("Full screen"),
                    transportRow,
                    Heading("Live patching"),
                    live.View,
                },
            },
        };

        // Opening sets the boxes to what is saved, so the handlers below hear only a hand.
        Flyout.Opening += (_, _) =>
        {
            dragToPan.IsChecked = canvas.DragToPan;
            scroll.SelectedIndex = (int)canvas.Scroll;
            live.Show(settings.Current);
            compactModules.IsChecked = canvas.CompactModules;
            endless.IsChecked = canvas.Endless;
            transportEdge.SelectedIndex = settings.Current.Transport == TransportEdge.Bottom ? 1 : 0;
            oversample.SelectedIndex = Math.Max(0, AudioRenderer.Oversamples.ToList().IndexOf(settings.Current.Oversample));
        };

        dragToPan.IsCheckedChanged += (_, _) => Change(page => page.DragToPan = dragToPan.IsChecked == true);
        scroll.SelectionChanged += (_, _) => Change(page => page.Scroll = (CanvasScroll)Math.Max(scroll.SelectedIndex, 0));
        compactModules.IsCheckedChanged += (_, _) => Change(page => page.CompactModules = compactModules.IsChecked == true);
        endless.IsCheckedChanged += (_, _) => Change(page => page.Endless = endless.IsChecked == true);

        transportEdge.SelectionChanged += (_, _) =>
        {
            if (!Flyout.IsOpen) return;

            settings.Current.Transport = transportEdge.SelectedIndex == 1 ? TransportEdge.Bottom : TransportEdge.Top;
            fullScreen.Apply(settings.Current);
            browser.Write(PageOutput.Section, PageOutput.Of(settings.Current).Json());
        };

        oversample.SelectionChanged += (_, _) =>
        {
            if (!Flyout.IsOpen || oversample.SelectedIndex < 0) return;

            settings.Current.Oversample = AudioRenderer.Oversamples[oversample.SelectedIndex];
            audio.Oversample = settings.Current.Oversample;
            browser.Write(PageOutput.Section, PageOutput.Of(settings.Current).Json());
        };
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

    /// <summary>A labeled picker with a note under it.</summary>
    private static StackPanel Row(ComboBox picker, string label, string note)
    {
        var row = new StackPanel
        {
            Spacing = 2,
            Margin = new Thickness(8, 0, 8, 4),
            Children =
            {
                InspectorRows.Field(label, picker),
                new TextBlock
                {
                    Text = note,
                    FontSize = Text.Small,
                    Foreground = Text.Muted,
                    TextWrapping = TextWrapping.Wrap,
                },
            },
        };

        return row;
    }

    private static Control Heading(string text)
    {
        var heading = SettingRows.Heading(text);
        heading.Margin = new Thickness(8, 6, 8, 0);
        return heading;
    }
}
