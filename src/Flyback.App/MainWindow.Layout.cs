using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Flyback.App;

/// <summary>
/// Leaving the window as it was left: size, monitor, panels and views, kept in
/// <see cref="WindowLayout"/> (ADR-0121).
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>Where the layout is kept, or null to keep it nowhere.</summary>
    private readonly string? layoutPath;

    /// <summary>The layout read at startup, then the one last written.</summary>
    private WindowLayout? layout;

    /// <summary>The last client size the window had while it was neither maximized nor full screen.</summary>
    private Size? normalSize;

    /// <summary>Size, state and monitor. Before the window is shown.</summary>
    private void ApplyWindowLayout()
    {
        // Only a drag of the frame: maximizing resizes the window too, and that is
        // not a size to come back to.
        Resized += (_, e) =>
        {
            if (e.Reason == WindowResizeReason.User && WindowState == WindowState.Normal) normalSize = e.ClientSize;
        };

        if (layout is not { } saved) return;

        if (saved.Width > 0 && saved.Height > 0)
        {
            Width = Math.Max(saved.Width, MinWidth);
            Height = Math.Max(saved.Height, MinHeight);
        }

        normalSize = new Size(Width, Height);

        if (saved.Maximized) WindowState = WindowState.Maximized;

        // The platform places the window, so which monitor it chose is only known
        // once it is up.
        Opened += (_, _) => ReturnToMonitor(saved.Monitor);
    }

    /// <summary>The panels and the views. After the first patch is on the canvas.</summary>
    private void ApplyPanelLayout()
    {
        if (layout is not { } saved || columns is null || previewRow is null || assistantColumn is null) return;

        columns.ColumnDefinitions[WideColumn].Width = new GridLength(saved.CanvasWeight, GridUnitType.Star);
        columns.ColumnDefinitions[SideColumn].Width = new GridLength(saved.SideWeight, GridUnitType.Star);

        previewShare = new GridLength(saved.PreviewWeight, GridUnitType.Star);
        if (previewBox is { IsVisible: true }) previewRow.Height = previewShare;
        columns.RowDefinitions[2].Height = new GridLength(saved.InspectorWeight, GridUnitType.Star);

        // A shown panel takes its width from the column and a hidden one from the
        // share, so both are set and the panel then put where it was.
        assistantShare = new GridLength(saved.AssistantWidth, GridUnitType.Pixel);
        if (assistant is { IsVisible: true }) assistantColumn.Width = assistantShare;
        assistantButton.IsChecked = saved.AssistantOpen && assistantButton.IsEnabled;

        controlsShare = new GridLength(saved.ControlsHeight, GridUnitType.Pixel);
        if (ControlsRow is { } controlsRow && controlsPanel.IsVisible) controlsRow.Height = controlsShare;
        ShowControls(saved.ControlsOpen);

        // Only while there is a picture to swap in, which is the button's own rule.
        swapButton.IsChecked = saved.Swapped && swapButton.IsEnabled;

        if (saved.Code) ShowCode(true);
    }

    /// <summary>Writes the layout down. A settings file is not worth a failure to close.</summary>
    private void RememberLayout()
    {
        if (layoutPath is null || columns is null) return;

        try
        {
            layout = CaptureLayout();
            layout.Save(layoutPath);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Could not save the window layout: {ex.Message}");
        }
    }

    private WindowLayout CaptureLayout()
    {
        // Full screen collapses every track, so the ones it put away are the layout.
        var away = previewIsFullScreen;

        double Column(int index) => Weight(away && columnsBefore is not null
            ? columnsBefore[index].Size
            : columns!.ColumnDefinitions[index].Width);

        double Row(int index) => Weight(away && rowsBefore is not null
            ? rowsBefore[index].Size
            : columns!.RowDefinitions[index].Height);

        // The row a panel stands in, whichever grid it is in while swapped. Full
        // screen zeroes the outer grid's rows only.
        double Under(Control? panel, Func<GridLength, double> measure) =>
            panel?.Parent == columns && away && rowsBefore is not null
                ? measure(rowsBefore[2].Size)
                : measure((panel?.Parent as Grid)?.RowDefinitions[2].Height ?? new GridLength(1, GridUnitType.Star));

        var state = away ? stateBefore : WindowState;
        var size = WindowState == WindowState.Normal ? ClientSize : normalSize;

        return new WindowLayout
        {
            Maximized = state == WindowState.Maximized,
            Width = size?.Width ?? layout?.Width ?? 0,
            Height = size?.Height ?? layout?.Height ?? 0,
            Monitor = Describe(Screens.ScreenFromWindow(this)) ?? layout?.Monitor,

            CanvasWeight = Column(WideColumn),
            SideWeight = Column(SideColumn),
            PreviewWeight = previewBox is { IsVisible: true } || away ? Row(0) : Weight(previewShare),
            InspectorWeight = Under(inspectorBox, Weight),

            AssistantWidth = assistant is { IsVisible: true } ? assistantColumn!.Width.Value : assistantShare.Value,
            AssistantOpen = assistant?.IsVisible == true,

            ControlsHeight = controlsPanel.IsVisible ? Under(controlsPanel, length => length.Value) : controlsShare.Value,
            ControlsOpen = controlsPanel.IsVisible,

            Code = showingCode,
            Swapped = swapButton.IsChecked == true,
        };

        static double Weight(GridLength length) => length.IsStar ? length.Value : 1;
    }

    private static WindowLayout.MonitorSpot? Describe(Screen? screen) => screen is null
        ? null
        : new()
        {
            Name = screen.DisplayName,
            X = screen.Bounds.X,
            Y = screen.Bounds.Y,
            Width = screen.Bounds.Width,
            Height = screen.Bounds.Height,
        };

    /// <summary>
    /// Moves the window to the monitor it was left on, if the platform put it
    /// somewhere else, and keeps it inside that monitor's usable area.
    /// </summary>
    /// <remarks>
    /// The offset from the corner of its monitor is kept rather than the window
    /// being centered, so copies the platform cascaded stay cascaded.
    /// </remarks>
    private void ReturnToMonitor(WindowLayout.MonitorSpot? wanted)
    {
        if (Screens.ScreenFromWindow(this) is not { } here) return;

        var target = (wanted is null ? null : Find(wanted, Screens.All)) ?? here;

        var state = WindowState;

        if (Same(target, here))
        {
            if (state == WindowState.Normal) Fit(target, Position);
            return;
        }

        // A maximized window does not move between monitors.
        if (state != WindowState.Normal) WindowState = WindowState.Normal;

        Fit(target, target.WorkingArea.Position + (Position - here.WorkingArea.Position));

        if (state != WindowState.Normal) WindowState = state;
    }

    /// <summary>Puts the window at <paramref name="at"/>, shrunk and shifted to fit inside <paramref name="screen"/>.</summary>
    private void Fit(Screen screen, PixelPoint at)
    {
        var area = screen.WorkingArea;
        var scale = screen.Scaling;

        var width = Math.Min(Width, area.Width / scale);
        var height = Math.Min(Height, area.Height / scale);

        if (width < Width) Width = Math.Max(width, MinWidth);
        if (height < Height) Height = Math.Max(height, MinHeight);

        var wide = (int)Math.Ceiling(Width * scale);
        var tall = (int)Math.Ceiling(Height * scale);

        Position = new PixelPoint(
            Math.Clamp(at.X, area.X, Math.Max(area.X, area.Right - wide)),
            Math.Clamp(at.Y, area.Y, Math.Max(area.Y, area.Bottom - tall)));
    }

    /// <summary>
    /// The monitor <paramref name="wanted"/> describes: the same name and place if
    /// there is one, else the same place, else the same name when it is the only one.
    /// </summary>
    private static Screen? Find(WindowLayout.MonitorSpot wanted, IReadOnlyList<Screen> all)
    {
        var name = string.IsNullOrEmpty(wanted.Name) ? null : wanted.Name;

        bool Place(Screen s) =>
            s.Bounds.X == wanted.X && s.Bounds.Y == wanted.Y
            && s.Bounds.Width == wanted.Width && s.Bounds.Height == wanted.Height;

        var both = all.Where(s => name is not null && s.DisplayName == name && Place(s)).ToList();
        if (both.Count == 1) return both[0];

        var places = all.Where(Place).ToList();
        if (places.Count == 1) return places[0];

        var names = all.Where(s => name is not null && s.DisplayName == name).ToList();
        return names.Count == 1 ? names[0] : null;
    }

    private static bool Same(Screen a, Screen b) => a.Bounds == b.Bounds && a.DisplayName == b.DisplayName;
}
