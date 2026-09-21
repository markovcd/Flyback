using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;

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
        Opened += (_, _) => MonitorPlacement.Return(this, saved.Monitor);
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

        // A splitter leaves star weights in pixels, far past what the file accepts.
        var (canvas, side) = Share(Column(WideColumn), Column(SideColumn),
            WindowLayout.DefaultCanvasWeight + WindowLayout.DefaultSideWeight);

        var (preview, inspector) = Share(
            previewBox is { IsVisible: true } || away ? Row(0) : Weight(previewShare),
            Under(inspectorBox, Weight),
            WindowLayout.DefaultPreviewWeight + WindowLayout.DefaultInspectorWeight);

        return new WindowLayout
        {
            Maximized = state == WindowState.Maximized,
            Width = size?.Width ?? layout?.Width ?? 0,
            Height = size?.Height ?? layout?.Height ?? 0,
            Monitor = MonitorPlacement.Describe(Screens.ScreenFromWindow(this)) ?? layout?.Monitor,

            CanvasWeight = canvas,
            SideWeight = side,
            PreviewWeight = preview,
            InspectorWeight = inspector,

            AssistantWidth = assistant is { IsVisible: true } ? assistantColumn!.Width.Value : assistantShare.Value,
            AssistantOpen = assistant?.IsVisible == true,

            ControlsHeight = controlsPanel.IsVisible ? Under(controlsPanel, length => length.Value) : controlsShare.Value,
            ControlsOpen = controlsPanel.IsVisible,

            Code = showingCode,
            Swapped = swapButton.IsChecked == true,
        };

        static double Weight(GridLength length) => length.IsStar ? length.Value : 1;

        static (double, double) Share(double a, double b, double total) =>
            a + b > 0 ? (a / (a + b) * total, b / (a + b) * total) : (total / 2, total / 2);
    }
}
