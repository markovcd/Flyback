using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Flyback.App.Controls;
using Flyback.App.Statistics;

namespace Flyback.App;

/// <summary>
/// The preview taking the whole window, or a whole monitor of its own, and giving it back.
/// </summary>
/// <remarks>
/// On the window's own monitor nothing is reparented: the preview stays where it is and
/// the shell around it is put away instead. The GPU surface is an <c>OpenGlControlBase</c>,
/// and moving one between parents tears its context down and builds it again — a picture
/// that blinked every time somebody wanted a closer look. On another monitor the move is
/// the point, so the preview goes to a window there and the renderer is built again once
/// each way (ADR-0129).
/// </remarks>
public sealed partial class MainWindow
{
    /// <summary>
    /// What each track was set to before the preview took over, in order.
    /// </summary>
    /// <remarks>
    /// The sizes are copied out and restored, rather than swapping in a new grid
    /// definition. That keeps the dragged layout when the preview is toggled.
    /// </remarks>
    private (GridLength Size, double Minimum)[]? columnsBefore;
    private (GridLength Size, double Minimum)[]? rowsBefore;

    /// <summary>
    /// Whether the window was maximised, or merely open, before it went full
    /// screen — the state Escape has to put back, which is not always Normal.
    /// </summary>
    private WindowState stateBefore;

    /// <summary>Which of the grid's children were showing before the preview took over.</summary>
    private Dictionary<Control, bool>? visibleBefore;

    /// <summary>Whether the preview currently has the window.</summary>
    private bool previewIsFullScreen;

    /// <summary>
    /// A track of no width at all, for the columns and rows the preview is not in.
    /// </summary>
    /// <remarks>
    /// Hiding a child is not enough: a grid track holds the width it was given whether
    /// or not anything visible stands in it. Zeroed rather than removed, because Grid
    /// indexes its definitions directly — a child left pointing at column four of a
    /// grid that now has one throws out of <c>MeasureOverride</c>.
    /// </remarks>
    private static GridLength None => new(0, GridUnitType.Pixel);

    private static GridLength Everything => new(1, GridUnitType.Star);

    /// <summary>Which monitor full screen fills — the Graphics section.</summary>
    private readonly ComboBox fullScreenOn = new Picker
    {
        Name = "fullScreenOn",
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>The monitors <see cref="fullScreenOn"/> lists after its first two rows, in order.</summary>
    private List<MonitorSpot> fullScreenMonitors = [];

    /// <summary>
    /// Lists the monitors plugged in now, and the chosen one if it is not, and
    /// selects what <paramref name="settings"/> says.
    /// </summary>
    private void ShowFullScreenSetting(OutputSettings settings)
    {
        var screens = Screens.All;

        fullScreenMonitors = [.. screens.Select(s => MonitorPlacement.Describe(s)!)];

        List<string> rows =
        [
            "Same monitor",
            "Another monitor",
            .. screens.Select(s => $"{s.DisplayName ?? "Monitor"} · {s.Bounds.Width}×{s.Bounds.Height}{(s.IsPrimary ? " · main" : "")}"),
        ];

        var chosen = settings.FullScreenMonitor is { } wanted ? MonitorPlacement.Find(wanted, fullScreenMonitors) : null;

        // Kept on the list while unplugged, so saving anything else does not forget it.
        if (settings.FullScreenMonitor is { } away && chosen is null)
        {
            fullScreenMonitors.Add(away);
            rows.Add($"{away.Name ?? "Monitor"} · {away.Width}×{away.Height} · not plugged in");
            chosen = fullScreenMonitors.Count - 1;
        }

        fullScreenOn.ItemsSource = rows;
        fullScreenOn.SelectedIndex = settings.FullScreen switch
        {
            FullScreenOn.OtherMonitor => 1,
            FullScreenOn.ChosenMonitor when chosen is { } row => 2 + row,
            _ => 0,
        };
    }

    /// <summary>What <see cref="fullScreenOn"/> holds, as the settings keep it.</summary>
    private (FullScreenOn On, MonitorSpot? Monitor) ReadFullScreenSetting() => fullScreenOn.SelectedIndex switch
    {
        1 => (FullScreenOn.OtherMonitor, outputSettings.FullScreenMonitor),
        >= 2 and var row when row - 2 < fullScreenMonitors.Count => (FullScreenOn.ChosenMonitor, fullScreenMonitors[row - 2]),
        _ => (FullScreenOn.SameMonitor, outputSettings.FullScreenMonitor),
    };

    /// <summary>The window holding the preview on another monitor, while it is there.</summary>
    private Window? pictureWindow;

    /// <summary>The knobs over the picture in <see cref="pictureWindow"/>.</summary>
    private StageKnobs? pictureKnobs;

    /// <summary>The dots and transport over the picture in <see cref="pictureWindow"/>.</summary>
    private TransportOverlay? pictureTransport;

    /// <summary>Goes full screen on the monitor the Graphics section names, or comes back.</summary>
    private void ToggleFullScreenPreview()
    {
        if (previewIsFullScreen || pictureWindow is not null)
        {
            LeaveFullScreen();
            return;
        }

        if (MonitorPlacement.FullScreenTarget(this, outputSettings.FullScreen, outputSettings.FullScreenMonitor) is { } screen)
            ShowPictureOn(screen);
        else
            ShowFullScreenPreview(true);
    }

    private void LeaveFullScreen()
    {
        pictureWindow?.Close();
        ShowFullScreenPreview(false);
    }

    /// <summary>
    /// Moves the preview to a full-screen window on <paramref name="screen"/>, leaving
    /// the editor as it is with a note where the picture was.
    /// </summary>
    internal void ShowPictureOn(Screen screen)
    {
        if (previewBox is null || pictureWindow is not null || previewIsFullScreen) return;

        usage.Count(Used.FullScreen);

        previewBox.Child = new TextBlock
        {
            Name = "pictureAway",
            Text = $"The picture is on {screen.DisplayName ?? "another monitor"}. Double-click here or press Esc to bring it back.",
            FontSize = Text.Small,
            Foreground = Text.Muted,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16),
        };

        preview.Renew();

        var knobs = pictureKnobs = new StageKnobs();

        knobs.Show(editor.Patch);
        knobs.Turning += TurnKnob;
        knobs.TurnEnded += document.LetGoOfKnob;

        var picture = new Panel();

        picture.Children.Add(preview);
        picture.Children.Add(knobs);

        // Not activated, so the keyboard stays with the editor.
        var window = pictureWindow = new Window
        {
            Title = "Flyback picture",
            Background = Brushes.Black,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 160,
            Height = 90,
            Content = picture,
        };

        var transport = pictureTransport = new TransportOverlay();

        transport.PauseClicked += TogglePause;
        transport.MuteClicked += ToggleMute;
        transport.RewindClicked += RewindToZero;

        picture.Children.Add(transport);

        window.DoubleTapped += (_, e) =>
        {
            window.Close();
            e.Handled = true;
        };

        window.KeyDown += (_, e) =>
        {
            var command = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;

            if (command && e.Key == Key.P) TogglePause();
            else if (e.Key == Key.Escape) window.Close();
            else return;

            e.Handled = true;
        };

        // Moved only once it is open: Windows tells a hidden window nothing when it
        // crosses to a monitor of another scale, and it would draw at the old one.
        window.Opened += (_, _) =>
        {
            window.Position = screen.Bounds.Position;
            window.WindowState = WindowState.FullScreen;
        };
        window.Closed += (_, _) => BringPictureBack(window);

        window.Show(this);
        SyncTransport();
        SyncStageKnobs();
    }

    private void BringPictureBack(Window window)
    {
        if (pictureWindow != window || previewBox is null) return;

        pictureWindow = null;
        pictureKnobs = null;
        pictureTransport = null;

        if (window.Content is Panel picture) picture.Children.Clear();
        window.Content = null;

        preview.Renew();
        previewBox.Child = preview;
    }

    /// <summary>Hands the window to the preview, or takes it back.</summary>
    private void ShowFullScreenPreview(bool full)
    {
        if (full == previewIsFullScreen) return;

        // All four arrive together when the layout is built, so this is one
        // question rather than four. Before that there is nothing to show.
        if (columns is null || previewBox is null || toolbar is null || statusBar is null) return;

        previewIsFullScreen = full;

        if (full) usage.Count(Used.FullScreen);

        if (full) Collapse();
        else Restore();

        toolbar.IsVisible = !full;
        statusBar.IsVisible = !full;

        // Remembered, since the assistant and the knobs stand in this grid and
        // are as often hidden as not.
        if (full) visibleBefore = columns.Children.ToDictionary(child => child, child => child.IsVisible);

        foreach (var child in columns.Children)
            child.IsVisible = full ? child == previewBox : visibleBefore?.GetValueOrDefault(child, true) ?? true;

        // The controls stand over whichever cell the preview is in.
        if (transportOverlay is { } overlay)
        {
            if (full)
            {
                Over(overlay);
                SyncTransport();
            }

            overlay.IsVisible = full;
        }

        Over(stageKnobs);
        SyncStageKnobs();

        // ShowPreview stands aside while the preview has the window, and the patch
        // may have lost its picture meanwhile. Only ever put away here: the row has
        // just been given back the height it was dragged to.
        if (!full && !HasPicture) ShowPreview(false);

        void Over(Control control)
        {
            if (!full) return;

            Grid.SetColumn(control, Grid.GetColumn(previewBox));
            Grid.SetRow(control, Grid.GetRow(previewBox));
            Grid.SetRowSpan(control, Grid.GetRowSpan(previewBox));
        }

        void Collapse()
        {
            stateBefore = WindowState;

            columnsBefore = [.. columns.ColumnDefinitions.Select(c => (c.Width, c.MinWidth))];
            rowsBefore = [.. columns.RowDefinitions.Select(r => (r.Height, r.MinHeight))];

            // Which track to leave standing is read off the layout rather than
            // written down here, since the preview is in the wide column while it
            // is swapped with the canvas and in the narrow one otherwise.
            var keepColumn = Grid.GetColumn(previewBox);
            var keepRow = Grid.GetRow(previewBox);

            for (var i = 0; i < columns.ColumnDefinitions.Count; i++)
            {
                var column = columns.ColumnDefinitions[i];

                // The minimum first: it outranks a width of nothing, and a column
                // zeroed while it still had one would hold that much of the shell
                // open across the picture.
                column.MinWidth = 0;
                column.Width = i == keepColumn ? Everything : None;
            }

            for (var i = 0; i < columns.RowDefinitions.Count; i++)
            {
                var row = columns.RowDefinitions[i];

                row.MinHeight = 0;
                row.Height = i == keepRow ? Everything : None;
            }

            WindowState = WindowState.FullScreen;
        }

        void Restore()
        {
            if (columnsBefore is { } savedColumns)
            {
                for (var i = 0; i < savedColumns.Length && i < columns.ColumnDefinitions.Count; i++)
                {
                    columns.ColumnDefinitions[i].Width = savedColumns[i].Size;
                    columns.ColumnDefinitions[i].MinWidth = savedColumns[i].Minimum;
                }
            }

            if (rowsBefore is { } savedRows)
            {
                for (var i = 0; i < savedRows.Length && i < columns.RowDefinitions.Count; i++)
                {
                    columns.RowDefinitions[i].Height = savedRows[i].Size;
                    columns.RowDefinitions[i].MinHeight = savedRows[i].Minimum;
                }
            }

            WindowState = stateBefore;
        }
    }
}
