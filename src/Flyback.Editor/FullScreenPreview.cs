using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Flyback.App.Bars;
using Flyback.App.Canvas;
using Flyback.App.Controls;
using Flyback.App.Knobs;
using Flyback.App.Settings;
using Flyback.App.Statistics;
using Flyback.App.Windows;

namespace Flyback.App;

/// <summary>
/// The preview taking the whole window, or a whole monitor of its own, and giving it back.
/// </summary>
/// <remarks>
/// On the window's own monitor nothing is reparented: the preview stays where it is and
/// the shell around it is put away instead. The GPU surface is an OpenGlControlBase,
/// and moving one between parents tears its context down and builds it again — a picture
/// that blinked every time somebody wanted a closer look. On another monitor the move is
/// the point, so the preview goes to a window there and the renderer is built again once
/// each way (ADR-0129).
/// </remarks>
internal sealed class FullScreenPreview(
    WindowHolder holder,
    PreviewHost preview,
    PanelKnobs knobs,
    Toolbar toolbar,
    StatusBar statusBar,
    NodeEditor editor,
    Playback playback,
    Document document,
    Usage usage,
    OutputSettingRepository settings,
    TransportControls transport)
{
    /// <summary>The grid the shell stands in, or null before the layout is built.</summary>
    public Grid? Columns { get; set; }

    /// <summary>The box the preview sits in, or null before the layout is built.</summary>
    public Border? PreviewBox { get; set; }

    /// <summary>Puts the preview's row away or back, for a patch that gained or lost its picture meanwhile.</summary>
    public Action<bool>? ShowPreview { get; set; }

    /// <summary>Whether the picture has all of the window or a window of its own.</summary>
    public bool IsAway => IsFullScreen || transport.PictureWindow is not null;

    /// <summary>
    /// What each track was set to before the preview took over, in order.
    /// </summary>
    /// <remarks>
    /// The sizes are copied out and restored, rather than swapping in a new grid
    /// definition. That keeps the dragged layout when the preview is toggled.
    /// </remarks>
    public (GridLength Size, double Minimum)[]? ColumnsBefore { get; private set; }
    public (GridLength Size, double Minimum)[]? RowsBefore { get; private set; }

    /// <summary>
    /// Whether the window was maximized, or merely open, before it went full
    /// screen — the state Escape has to put back, which is not always Normal.
    /// </summary>
    public WindowState StateBefore { get; private set; }

    /// <summary>Which of the grid's children were showing before the preview took over.</summary>
    private Dictionary<Control, bool>? visibleBefore;

    /// <summary>Whether the preview currently has the window.</summary>
    public bool IsFullScreen { get; private set; }

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

    /// <summary>Goes full screen on the monitor the Graphics section names, or comes back.</summary>
    public void Toggle()
    {
        if (IsFullScreen || transport.PictureWindow is not null)
        {
            Leave();
            return;
        }

        if (holder.Instance is Window window
            && MonitorPlacement.FullScreenTarget(window, settings.Current.FullScreen, settings.Current.FullScreenMonitor) is { } screen)
            ShowPictureOn(screen);
        else
            Show(true);
    }

    public void Leave()
    {
        transport.PictureWindow?.Close();
        Show(false);
    }

    /// <summary>
    /// Moves the preview to a full-screen window on <paramref name="screen"/>, leaving
    /// the editor as it is with a note where the picture was.
    /// </summary>
    public void ShowPictureOn(Screen screen)
    {
        if (PreviewBox is not { } previewBox || transport.PictureWindow is not null || IsFullScreen) return;

        var owner = holder.Window;

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

        var window = transport.PictureWindow = new PictureWindow(screen, preview);

        knobs.Away = window.Knobs;
        window.Knobs.Show(editor.History.Patch);
        window.Knobs.Turning += knobs.Turn;
        window.Knobs.TurnEnded += document.LetGoOfKnob;

        window.Transport.PauseClicked += transport.TogglePause;
        window.Transport.MuteClicked += playback.ToggleMute;
        window.Transport.RewindClicked += playback.RewindPressed;

        toolbar.Seek.Drive(window.Transport);
        TransportOverlay.Lay(settings.Current.Transport, window.Transport, window.Knobs);

        window.PauseRequested += (_, _) => transport.TogglePause();
        window.StatsRequested += (_, _) => transport.ToggleStats(IsFullScreen);
        window.Closed += (_, _) => BringPictureBack(window);

        window.Show(owner);
        transport.Sync();
        transport.SyncStats(IsFullScreen);
        knobs.SyncStages();
    }

    private void BringPictureBack(PictureWindow window)
    {
        if (transport.PictureWindow != window || PreviewBox is not { } previewBox) return;

        transport.PictureWindow = null;
        knobs.Away = null;
        toolbar.Seek.Drop(window.Transport);

        preview.Renew();
        previewBox.Child = preview;
    }

    /// <summary>Hands the window to the preview, or takes it back.</summary>
    public void Show(bool full)
    {
        if (full == IsFullScreen) return;

        // All four arrive together when the layout is built, so this is one
        // question rather than four. Before that there is nothing to show.
        if (Columns is not { } columns || PreviewBox is not { } previewBox) return;

        // A page has no window state to change: the preview takes the page.
        var owner = holder.Instance as Window;

        IsFullScreen = full;
        knobs.OverPicture = full;

        if (full) usage.Count(Used.FullScreen);

        if (full) Collapse();
        else Restore();

        toolbar.View.IsVisible = !full;
        statusBar.View.IsVisible = !full;

        // Remembered, since the assistant and the knobs stand in this grid and
        // are as often hidden as not.
        if (full) visibleBefore = columns.Children.ToDictionary(child => child, child => child.IsVisible);

        foreach (var child in columns.Children)
            child.IsVisible = full ? child == previewBox : visibleBefore?.GetValueOrDefault(child, true) ?? true;

        // The controls stand over whichever cell the preview is in.
        if (transport.Overlay is { } overlay)
        {
            if (full)
            {
                Over(overlay);
                transport.Sync();
            }

            overlay.IsVisible = full;
        }

        Over(knobs.Stage);
        knobs.SyncStages();

        if (transport.Stats is not null) Over(transport.Stats);
        transport.SyncStats(IsFullScreen);

        // The keyboard goes with what is showing: the canvas it was on is put away.
        if (full) previewBox.Focus();
        else editor.Focus();

        // ShowPreview stands aside while the preview has the window, and the patch
        // may have lost its picture meanwhile. Only ever put away here: the row has
        // just been given back the height it was dragged to.
        if (!full && !playback.HasPicture) ShowPreview?.Invoke(false);

        void Over(Control control)
        {
            if (!full) return;

            Grid.SetColumn(control, Grid.GetColumn(previewBox));
            Grid.SetRow(control, Grid.GetRow(previewBox));
            Grid.SetRowSpan(control, Grid.GetRowSpan(previewBox));
        }

        void Collapse()
        {
            StateBefore = owner?.WindowState ?? WindowState.Normal;

            ColumnsBefore = [.. columns.ColumnDefinitions.Select(c => (c.Width, c.MinWidth))];
            RowsBefore = [.. columns.RowDefinitions.Select(r => (r.Height, r.MinHeight))];

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

            if (owner is not null) owner.WindowState = WindowState.FullScreen;
        }

        void Restore()
        {
            if (ColumnsBefore is { } savedColumns)
            {
                for (var i = 0; i < savedColumns.Length && i < columns.ColumnDefinitions.Count; i++)
                {
                    columns.ColumnDefinitions[i].Width = savedColumns[i].Size;
                    columns.ColumnDefinitions[i].MinWidth = savedColumns[i].Minimum;
                }
            }

            if (RowsBefore is { } savedRows)
            {
                for (var i = 0; i < savedRows.Length && i < columns.RowDefinitions.Count; i++)
                {
                    columns.RowDefinitions[i].Height = savedRows[i].Size;
                    columns.RowDefinitions[i].MinHeight = savedRows[i].Minimum;
                }
            }

            if (owner is not null) owner.WindowState = StateBefore;
        }
    }
}
