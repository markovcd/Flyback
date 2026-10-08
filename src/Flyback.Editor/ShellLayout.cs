using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Flyback.Editor.Assist;
using Flyback.Ui.Audio;
using Flyback.Editor.Bars;
using Flyback.Editor.Canvas;
using Flyback.Editor.Controls;
using Flyback.Ui.Controls;
using Flyback.Editor.Inspect;
using Flyback.Editor.Knobs;
using Flyback.Editor.Notices;
using Flyback.Editor.Settings;
using Flyback.Editor.Statistics;
using Flyback.Editor.Windows;
using Colors = Flyback.Ui.Controls.Colors;

namespace Flyback.Editor;

/// <summary>The editor's grid and the panels and views arranged in it.</summary>
internal sealed class ShellLayout(
    Toolbar toolbar,
    TransportRow row,
    StatusBar statusBar,
    NodeEditor editor,
    SourceView source,
    PanelKnobs knobs,
    AssistantPanel assistant,
    Inspector inspector,
    PreviewHost preview,
    IAudioEngine audio,
    Playback playback,
    TransportControls transport,
    FullScreenPreview fullScreen,
    OutputSettingRepository settings,
    Document document,
    Usage usage,
    WindowLayoutKeeper layoutKeeper,
    EditorHost host,
    LastPress lastPress)
    : IReactTo<PatchCompiled>,
        IReactTo<GestureFinished>,
        IReactTo<KnobsWanted>,
        IReactTo<KnobsAsked>,
        IReactTo<SwapAsked>,
        IReactTo<AssistantAsked>,
        IReactTo<SideAsked>,
        IReactTo<TransportAsked>
{
    private Grid? columns;
    private Border? previewBox;
    private RowDefinition? previewRow;
    private GridSplitter? previewSplitter;
    private GridLength previewShare = new(1, GridUnitType.Star);
    private Grid? patchPane;
    private Border? inspectorBox;
    private const int WideColumn = 2;
    private const int SideColumn = 4;
    private bool previewHideWaiting;
    private bool swapped;
    private GridSplitter? sideSplitter;
    private GridLength sideShare = new(WindowLayout.DefaultSideWeight, GridUnitType.Star);
    private GridLength canvasShare = new(3, GridUnitType.Star);
    private bool sideShown = true;
    private const double CanvasMinWidth = 280;
    private const double SideMinWidth = 300;

    /// <summary>
    /// Whether the window is too narrow for the canvas and the side column side by side, as a
    /// phone held upright is, so the side column is shown in the canvas's place or not at all.
    /// </summary>
    private bool narrow;

    /// <summary>Whether the side column was shown when the window went narrow, to be put back when it widens.</summary>
    private bool sideBeforeNarrow = true;
    private ColumnDefinition? assistantColumn;
    private GridSplitter? assistantSplitter;
    private GridLength assistantShare = new(WindowLayout.DefaultAssistantWidth, GridUnitType.Pixel);
    private readonly GridSplitter controlsSplitter = Splitter(across: false, "controls-splitter");
    private GridLength controlsShare = new(WindowLayout.DefaultControlsHeight);

    /// <summary>The gap a splitter keeps between the panels it divides.</summary>
    internal const double SplitterGap = 5;

    /// <summary>How far past its gap, either side, a splitter can be taken hold of: a fingertip's worth.</summary>
    internal const double SplitterReach = 8;

    /// <summary>
    /// A splitter that keeps a <see cref="SplitterGap"/> between its panels but reaches
    /// <see cref="SplitterReach"/> over each, on top of them, so a finger finds it.
    /// </summary>
    /// <param name="across">Whether it drags left and right, between two columns.</param>
    private static GridSplitter Splitter(bool across, string name)
    {
        var splitter = new GridSplitter { Name = name, Background = Brushes.Transparent, ZIndex = 1 };
        var held = SplitterGap + 2 * SplitterReach;

        if (across)
        {
            splitter.Width = held;
            splitter.Margin = new Thickness(-SplitterReach, 0);
        }
        else
        {
            splitter.Height = held;
            splitter.Margin = new Thickness(0, -SplitterReach);
        }

        return splitter;
    }

    public bool PreviewHideWaiting => previewHideWaiting;
    public bool IsBuilt => columns is not null;

    public Task On(PatchCompiled notice)
    {
        ShowPreview(playback.HasPicture);
        return Task.CompletedTask;
    }

    /// <summary>The preview's row was held while a gesture was under way, and can go now.</summary>
    public Task On(GestureFinished notice)
    {
        if (previewHideWaiting) ShowPreview(playback.HasPicture);

        return Task.CompletedTask;
    }

    public Task On(KnobsWanted notice)
    {
        ShowControls(true);
        return Task.CompletedTask;
    }

    public Task On(KnobsAsked notice)
    {
        ShowControls(notice.Shown);
        return Task.CompletedTask;
    }

    public Task On(SwapAsked notice)
    {
        SwapPreview(notice.Swapped);
        return Task.CompletedTask;
    }

    public Task On(AssistantAsked notice)
    {
        ShowAssistant(notice.Shown);
        return Task.CompletedTask;
    }

    public Task On(SideAsked notice)
    {
        ShowSide(notice.Shown);
        return Task.CompletedTask;
    }

    public Task On(TransportAsked notice)
    {
        row.Shown = notice.Shown;
        return Task.CompletedTask;
    }

    public Control Build()
    {
        editor.Tags.Types = assistant.Undescribed;

        var root = new DockPanel();
        lastPress.Watch(root);
        DockPanel.SetDock(toolbar.View, Dock.Top);
        DockPanel.SetDock(statusBar.View, Dock.Bottom);
        DockPanel.SetDock(row.View, Dock.Bottom);

        columns = new Grid
        {
            Name = "columns",
            ColumnDefinitions =
            [
                new ColumnDefinition(assistantShare),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(canvasShare) { MinWidth = CanvasMinWidth },
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(new GridLength(1.6, GridUnitType.Star)) { MinWidth = SideMinWidth },
            ],
            RowDefinitions =
            [
                new RowDefinition(new GridLength(1, GridUnitType.Star)) { MinHeight = 140 },
                new RowDefinition(GridLength.Auto),
                new RowDefinition(new GridLength(1.1, GridUnitType.Star)) { MinHeight = 120 },
            ],
        };

        fullScreen.Columns = columns;
        assistantColumn = columns.ColumnDefinitions[0];
        assistantSplitter = Splitter(across: true, "assistant-splitter");

        patchPane = new Grid
        {
            RowDefinitions =
            [
                new RowDefinition(new GridLength(2.2, GridUnitType.Star)) { MinHeight = 160 },
                new RowDefinition(GridLength.Auto),
                new RowDefinition(new GridLength(0)),
            ],
        };

        Grid.SetRow(editor, 0);
        Grid.SetRow(source, 0);
        Grid.SetRow(controlsSplitter, 1);
        controlsSplitter.IsVisible = false;
        Grid.SetRow(knobs.View, 2);
        patchPane.Children.Add(editor);
        patchPane.Children.Add(source);
        patchPane.Children.Add(controlsSplitter);
        patchPane.Children.Add(knobs.View);

        foreach (var (child, column) in new (Control, int)[]
                 {
                     (assistant, 0),
                     (assistantSplitter, 1),
                     (patchPane, WideColumn),
                     (sideSplitter = Splitter(across: true, "side-splitter"), 3),
                 })
        {
            Grid.SetColumn(child, column);
            Grid.SetRowSpan(child, 3);
            columns.Children.Add(child);
        }

        BuildRightPanel(columns, SideColumn);
        ShowAssistant(false);

        columns.SizeChanged += (_, e) => Narrow(e.NewSize.Width < CanvasMinWidth + SideMinWidth);

        root.Children.Add(toolbar.View);
        root.Children.Add(statusBar.View);
        root.Children.Add(row.View);
        root.Children.Add(columns);
        fullScreen.ShowPreview = ShowPreview;
        return root;
    }

    public void ShowAssistant(bool shown)
    {
        if (assistantColumn is null || assistantSplitter is null) return;
        if (toolbar.Assistant.IsChecked != shown) toolbar.Assistant.IsChecked = shown;
        if (shown == assistant.IsVisible) return;
        if (!shown && assistant.IsVisible) assistantShare = assistantColumn.Width;
        assistant.IsVisible = shown;
        assistantSplitter.IsVisible = shown;
        assistantColumn.MinWidth = shown ? 280d : 0d;
        assistantColumn.Width = shown ? assistantShare : new GridLength(0);
    }

    /// <summary>
    /// Takes the column beside the canvas away, or brings it back at the width it left with.
    /// It holds the canvas while the two are swapped, and then it stays.
    /// </summary>
    public void ShowSide(bool shown)
    {
        if (columns is null || sideSplitter is null) return;
        if (swapped) shown = true;
        if (shown != sideShown && !fullScreen.IsFullScreen)
        {
            if (!shown) sideShare = columns.ColumnDefinitions[SideColumn].Width;
            sideShown = shown;
            LaySide();
        }
        if (toolbar.Side.IsChecked != sideShown) toolbar.Side.IsChecked = sideShown;
    }

    /// <summary>Sizes the canvas's column and the side column for whether the side is shown, and whether the window is narrow.</summary>
    private void LaySide()
    {
        if (columns is null || sideSplitter is null) return;

        var canvas = columns.ColumnDefinitions[WideColumn];
        var side = columns.ColumnDefinitions[SideColumn];
        var canvasGone = narrow && sideShown;

        if (canvasGone && canvas.Width.Value > 0) canvasShare = canvas.Width;

        canvas.MinWidth = canvasGone ? 0d : CanvasMinWidth;
        canvas.Width = canvasGone ? new GridLength(0) : canvasShare;
        sideSplitter.IsVisible = sideShown && !narrow;
        side.MinWidth = sideShown && !narrow ? SideMinWidth : 0d;
        side.Width = sideShown ? sideShare : new GridLength(0);
    }

    /// <summary>
    /// Goes to one column or back to two as the window crosses the width the two need. One
    /// column opens on the canvas, and the side button trades it for the preview and the
    /// inspector; swapping those two needs both columns, so it waits for a wider window.
    /// </summary>
    private void Narrow(bool value)
    {
        if (value == narrow || columns is null || fullScreen.IsFullScreen) return;

        if (value)
        {
            sideBeforeNarrow = sideShown;
            if (sideShown) sideShare = columns.ColumnDefinitions[SideColumn].Width;
            toolbar.Swap.IsChecked = false;
        }

        narrow = value;
        sideShown = !value && sideBeforeNarrow;
        LaySide();

        if (toolbar.Side.IsChecked != sideShown) toolbar.Side.IsChecked = sideShown;
        ToolTip.SetTip(toolbar.Side, narrow ? Toolbar.SideNarrowTip : Toolbar.SideTip);
        ShowPreview(playback.HasPicture);
    }

    public void ShowPreview(bool shown)
    {
        previewHideWaiting = !shown && toolbar.Swap.IsChecked == true && editor.Gestures.Gesturing;
        if (previewHideWaiting) return;

        toolbar.Swap.IsEnabled = shown && !narrow;
        ToolTip.SetTip(toolbar.Swap, !shown ? Toolbar.NoPictureToSwapTip : narrow ? Toolbar.NarrowSwapTip : Toolbar.SwapTip);
        if (fullScreen.IsFullScreen || previewBox is null || previewRow is null || previewSplitter is null) return;
        if (!shown) toolbar.Swap.IsChecked = false;
        if (!shown && previewBox.IsVisible) previewShare = previewRow.Height;
        previewBox.IsVisible = shown;
        previewSplitter.IsVisible = shown;
        previewRow.MinHeight = shown ? 140d : 0d;
        previewRow.Height = shown ? previewShare : new GridLength(0);
    }

    /// <summary>
    /// Holds the picture's row to the height its picture fills at the row's width, so a tall,
    /// narrow side column gives the room to the inspector rather than to black above and below.
    /// </summary>
    private void FitPreviewRow()
    {
        if (previewRow is null || previewBox is not { } box || !ReferenceEquals(box.Parent, columns)) return;

        // Full screen hands the row the whole window, letterbox and all.
        if (fullScreen.IsFullScreen)
        {
            previewRow.MaxHeight = double.PositiveInfinity;
            return;
        }

        var size = preview.Resolution;
        if (size.Width <= 0 || size.Height <= 0 || box.Bounds.Width <= 0) return;

        var fits = box.Bounds.Width * size.Height / size.Width;
        if (Math.Abs(previewRow.MaxHeight - fits) > 0.5) previewRow.MaxHeight = fits;
    }

    public void SwapPreview(bool swapped)
    {
        if (columns is null || previewBox is null || patchPane is null || inspectorBox is null || previewSplitter is null) return;
        if (swapped == (Grid.GetColumn(previewBox) == WideColumn)) return;

        this.swapped = swapped;
        if (swapped) ShowSide(true);
        toolbar.Side.IsEnabled = !swapped;
        ToolTip.SetTip(toolbar.Side, swapped ? Toolbar.SideSwappedTip : Toolbar.SideTip);

        var (pictureColumn, patchColumn) = swapped ? (WideColumn, SideColumn) : (SideColumn, WideColumn);
        Grid.SetColumn(previewBox, pictureColumn);
        Grid.SetColumn(patchPane, patchColumn);
        Hang(swapped ? controlsSplitter : previewSplitter, columns, pictureColumn, 1);
        Hang(swapped ? knobs.View : inspectorBox, columns, pictureColumn, 2);
        Hang(swapped ? previewSplitter : controlsSplitter, patchPane, 0, 1);
        Hang(swapped ? inspectorBox : knobs.View, patchPane, 0, 2);

        var outer = columns.RowDefinitions[2];
        var inner = patchPane.RowDefinitions[2];
        (outer.MinHeight, inner.MinHeight) = (inner.MinHeight, outer.MinHeight);
        (outer.Height, inner.Height) = (inner.Height, outer.Height);
        if (swapped) usage.Count(Used.Swapped);

        static void Hang(Control child, Grid grid, int column, int row)
        {
            if (child.Parent != grid)
            {
                (child.Parent as Panel)?.Children.Remove(child);
                grid.Children.Add(child);
            }
            Grid.SetColumn(child, column);
            Grid.SetRow(child, row);
        }
    }

    public void BuildRightPanel(Grid grid, int column)
    {
        previewBox = fullScreen.PreviewBox = new Border { Background = Brushes.Black, Child = preview, Focusable = true };
        KeyboardNavigation.SetIsTabStop(previewBox, false);
        PictureTaps.Attach(previewBox, transport.TogglePause, () =>
        {
            if (!host.InPage) fullScreen.Toggle();
        });
        Grid.SetColumn(previewBox, column);
        Grid.SetRow(previewBox, 0);
        previewRow = grid.RowDefinitions[0];
        previewBox.SizeChanged += (_, _) => FitPreviewRow();
        preview.ResolutionChanged += FitPreviewRow;

        var splitter = previewSplitter = Splitter(across: false, "preview-splitter");
        Grid.SetColumn(splitter, column);
        Grid.SetRow(splitter, 1);

        var plateHost = inspector.PlateHost;
        var wash = inspector.Wash;

        // The plate scrolls with the rows it heads, and the header pins in once it has gone
        // or where there is no room for it at all.
        var rows = new StackPanel { Children = { plateHost, inspector.Panel } };
        var scroller = new ScrollViewer { Content = rows, Background = Brushes.Transparent };
        var pinned = new Decorator();
        var scrolling = new Panel { Children = { scroller, inspector.Header } };
        DockPanel.SetDock(pinned, Dock.Top);
        var reading = new DockPanel { Children = { pinned, scrolling } };

        var inspectorBorder = inspectorBox = new Border
        {
            Background = new SolidColorBrush(Colors.Panel),
            Child = new Panel { Children = { wash, reading } },
        };
        _ = new InspectorFold(reading, scroller, rows, pinned, plateHost, inspector.Header, wash, inspector.Panel, lastPress);
        Grid.SetColumn(inspectorBorder, column);
        Grid.SetRow(inspectorBorder, 2);

        var overlay = transport.Overlay = new TransportOverlay { IsVisible = false };
        overlay.PauseClicked += transport.TogglePause;
        overlay.MuteClicked += playback.ToggleMute;
        overlay.RewindClicked += playback.RewindPressed;
        transport.Stats = new StatsOverlay(preview, audio, () => playback.HasSound);
        row.Seek.Drive(overlay);
        TransportOverlay.Lay(settings.Current.Transport, overlay, knobs.Stage);
        grid.Children.Add(previewBox);
        grid.Children.Add(transport.Stats);
        grid.Children.Add(knobs.Stage);
        grid.Children.Add(overlay);
        grid.Children.Add(splitter);
        grid.Children.Add(inspectorBorder);
    }

    public void ShowControls(bool shown)
    {
        if (fullScreen.IsFullScreen) return;
        var panel = knobs.View;
        if (ControlsRow is { } row && shown != panel.IsVisible)
        {
            if (!shown && panel.IsVisible) controlsShare = row.Height;
            row.MinHeight = shown ? 60d : 0d;
            row.Height = shown ? controlsShare : new GridLength(0);
        }
        panel.IsVisible = shown;
        controlsSplitter.IsVisible = shown;
        if (toolbar.Knobs.IsChecked != shown) toolbar.Knobs.IsChecked = shown;
        if (!shown) knobs.Link(null);
    }

    private RowDefinition? ControlsRow => knobs.View.Parent is Grid grid ? grid.RowDefinitions[2] : null;

    public void ApplyPanelLayout()
    {
        if (layoutKeeper.Saved is not { } saved || columns is null || previewRow is null || assistantColumn is null) return;
        canvasShare = new GridLength(saved.CanvasWeight, GridUnitType.Star);
        if (!(narrow && sideShown)) columns.ColumnDefinitions[WideColumn].Width = canvasShare;
        sideShare = new GridLength(saved.SideWeight, GridUnitType.Star);
        if (sideShown) columns.ColumnDefinitions[SideColumn].Width = sideShare;
        previewShare = new GridLength(saved.PreviewWeight, GridUnitType.Star);
        if (previewBox is { IsVisible: true }) previewRow.Height = previewShare;
        columns.RowDefinitions[2].Height = new GridLength(saved.InspectorWeight, GridUnitType.Star);
        assistantShare = new GridLength(saved.AssistantWidth, GridUnitType.Pixel);
        if (assistant.IsVisible) assistantColumn.Width = assistantShare;
        toolbar.Assistant.IsChecked = saved.AssistantOpen && toolbar.Assistant.IsEnabled;
        controlsShare = new GridLength(saved.ControlsHeight, GridUnitType.Pixel);
        if (ControlsRow is { } row && knobs.View.IsVisible) row.Height = controlsShare;
        ShowControls(saved.ControlsOpen);
        if (narrow) sideBeforeNarrow = saved.SideOpen;
        else ShowSide(saved.SideOpen);
        toolbar.Swap.IsChecked = saved.Swapped && toolbar.Swap.IsEnabled;
        toolbar.Transport.IsChecked = saved.TransportOpen;
        if (saved.Code) document.ShowCode(true);
    }

    public WindowLayout Capture(Window window)
    {
        var away = fullScreen.IsFullScreen;
        double Column(int index) => Weight(away && fullScreen.ColumnsBefore is not null
            ? fullScreen.ColumnsBefore[index].Size : columns!.ColumnDefinitions[index].Width);
        double Row(int index) => Weight(away && fullScreen.RowsBefore is not null
            ? fullScreen.RowsBefore[index].Size : columns!.RowDefinitions[index].Height);
        double Under(Control? panel, Func<GridLength, double> measure) =>
            panel?.Parent == columns && away && fullScreen.RowsBefore is not null
                ? measure(fullScreen.RowsBefore[2].Size)
                : measure((panel?.Parent as Grid)?.RowDefinitions[2].Height ?? new GridLength(1, GridUnitType.Star));

        var state = away ? fullScreen.StateBefore : window.WindowState;
        var size = window.WindowState == WindowState.Normal ? window.ClientSize : layoutKeeper.NormalSize;
        var (canvas, side) = Share(narrow && sideShown ? Weight(canvasShare) : Column(WideColumn), sideShown ? Column(SideColumn) : Weight(sideShare), WindowLayout.DefaultCanvasWeight + WindowLayout.DefaultSideWeight);
        var (previewWeight, inspectorWeight) = Share(
            previewBox is { IsVisible: true } || away ? Row(0) : Weight(previewShare),
            Under(inspectorBox, Weight), WindowLayout.DefaultPreviewWeight + WindowLayout.DefaultInspectorWeight);

        return new WindowLayout
        {
            Maximized = state == WindowState.Maximized,
            Width = size?.Width ?? layoutKeeper.Saved?.Width ?? 0,
            Height = size?.Height ?? layoutKeeper.Saved?.Height ?? 0,
            Monitor = MonitorPlacement.Describe(window.Screens.ScreenFromWindow(window)) ?? layoutKeeper.Saved?.Monitor,
            CanvasWeight = canvas,
            SideWeight = side,
            PreviewWeight = previewWeight,
            InspectorWeight = inspectorWeight,
            AssistantWidth = assistant.IsVisible ? assistantColumn!.Width.Value : assistantShare.Value,
            AssistantOpen = assistant.IsVisible,
            ControlsHeight = knobs.View.IsVisible ? Under(knobs.View, length => length.Value) : controlsShare.Value,
            ControlsOpen = knobs.View.IsVisible,
            Code = document.ShowingCode,
            Swapped = toolbar.Swap.IsChecked == true,
            SideOpen = narrow ? sideBeforeNarrow : sideShown,
            TransportOpen = row.Shown,
        };

        static double Weight(GridLength length) => length.IsStar ? length.Value : 1;
        static (double, double) Share(double a, double b, double total) =>
            a + b > 0 ? (a / (a + b) * total, b / (a + b) * total) : (total / 2, total / 2);
    }
}
