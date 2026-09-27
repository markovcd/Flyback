using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.App.Assist;
using Flyback.App.Bars;
using Flyback.App.Canvas;
using Flyback.App.Controls;
using Flyback.App.Inspect;
using Flyback.App.Knobs;
using Flyback.App.Settings;
using Flyback.App.Statistics;
using Flyback.App.Windows;
using Colors = Flyback.App.Controls.Colors;

namespace Flyback.App;

/// <summary>The editor's grid and the panels and views arranged in it.</summary>
internal sealed class ShellLayout(
    Toolbar toolbar,
    StatusBar statusBar,
    NodeEditor editor,
    SourceView source,
    PanelKnobs knobs,
    AssistantPanel assistant,
    Inspector inspector,
    PreviewHost preview,
    Playback playback,
    TransportControls transport,
    FullScreenPreview fullScreen,
    OutputSettingRepository settings,
    Document document,
    Usage usage,
    WindowLayoutKeeper layoutKeeper)
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
    private ColumnDefinition? assistantColumn;
    private GridSplitter? assistantSplitter;
    private GridLength assistantShare = new(WindowLayout.DefaultAssistantWidth, GridUnitType.Pixel);
    private readonly GridSplitter controlsSplitter = new()
    {
        Name = "controls-splitter",
        Background = Brushes.Transparent,
        Height = 5,
        IsVisible = false,
    };
    private GridLength controlsShare = new(WindowLayout.DefaultControlsHeight);

    public bool PreviewHideWaiting => previewHideWaiting;
    public bool IsBuilt => columns is not null;

    public Control Build(EditState editState)
    {
        assistant.ConversationChanged += (_, _) => editState.Refresh();
        editor.Tags.Types = assistant.Undescribed;
        assistant.UndescribedChanged += (_, _) =>
        {
            editor.Tags.Types = assistant.Undescribed;
            inspector.Build();
        };

        var root = new DockPanel();
        DockPanel.SetDock(toolbar.View, Dock.Top);
        DockPanel.SetDock(statusBar.View, Dock.Bottom);

        columns = new Grid
        {
            Name = "columns",
            ColumnDefinitions =
            [
                new ColumnDefinition(assistantShare),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(new GridLength(3, GridUnitType.Star)) { MinWidth = 280 },
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(new GridLength(1.6, GridUnitType.Star)) { MinWidth = 300 },
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
        assistantSplitter = new GridSplitter { Background = Brushes.Transparent, Width = 5 };

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
                     (new GridSplitter { Width = 5, Background = Brushes.Transparent }, 3),
                 })
        {
            Grid.SetColumn(child, column);
            Grid.SetRowSpan(child, 3);
            columns.Children.Add(child);
        }

        BuildRightPanel(columns, SideColumn);
        ShowAssistant(false);

        root.Children.Add(toolbar.View);
        root.Children.Add(statusBar.View);
        root.Children.Add(columns);
        fullScreen.ShowPreview = ShowPreview;
        return root;
    }

    public void ShowAssistant(bool shown)
    {
        if (assistantColumn is null || assistantSplitter is null) return;
        if (!shown && assistant.IsVisible) assistantShare = assistantColumn.Width;
        assistant.IsVisible = shown;
        assistantSplitter.IsVisible = shown;
        assistantColumn.MinWidth = shown ? 280d : 0d;
        assistantColumn.Width = shown ? assistantShare : new GridLength(0);
    }

    public void ShowPreview(bool shown)
    {
        previewHideWaiting = !shown && toolbar.Swap.IsChecked == true && editor.Gestures.Gesturing;
        if (previewHideWaiting) return;

        toolbar.Swap.IsEnabled = shown;
        ToolTip.SetTip(toolbar.Swap, shown ? Toolbar.SwapTip : Toolbar.NoPictureToSwapTip);
        if (fullScreen.IsFullScreen || previewBox is null || previewRow is null || previewSplitter is null) return;
        if (!shown) toolbar.Swap.IsChecked = false;
        if (!shown && previewBox.IsVisible) previewShare = previewRow.Height;
        previewBox.IsVisible = shown;
        previewSplitter.IsVisible = shown;
        previewRow.MinHeight = shown ? 140d : 0d;
        previewRow.Height = shown ? previewShare : new GridLength(0);
    }

    public void SwapPreview(bool swapped)
    {
        if (columns is null || previewBox is null || patchPane is null || inspectorBox is null || previewSplitter is null) return;
        if (swapped == (Grid.GetColumn(previewBox) == WideColumn)) return;

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
        previewBox.DoubleTapped += (_, e) =>
        {
            fullScreen.Toggle();
            e.Handled = true;
        };
        Grid.SetColumn(previewBox, column);
        Grid.SetRow(previewBox, 0);
        previewRow = grid.RowDefinitions[0];

        var splitter = previewSplitter = new GridSplitter { Background = Brushes.Transparent, Height = 5 };
        Grid.SetColumn(splitter, column);
        Grid.SetRow(splitter, 1);

        var reading = new DockPanel();
        var plateHost = inspector.PlateHost;
        var wash = inspector.Wash;
        DockPanel.SetDock(plateHost, Dock.Top);
        reading.Children.Add(plateHost);
        reading.Children.Add(new ScrollViewer { Content = inspector.Panel, Background = Brushes.Transparent });

        var inspectorBorder = inspectorBox = new Border
        {
            Background = new SolidColorBrush(Colors.Panel),
            Child = new Panel { Children = { wash, reading } },
        };
        plateHost.PropertyChanged += (_, e) =>
        {
            if (e.Property != Visual.BoundsProperty) return;
            wash.Below = plateHost.Bounds.Height;
            wash.BandHeight = (plateHost.Content as ModulePlate)?.Band ?? 0;
        };
        Grid.SetColumn(inspectorBorder, column);
        Grid.SetRow(inspectorBorder, 2);

        var overlay = transport.Overlay = new TransportOverlay { IsVisible = false };
        overlay.PauseClicked += transport.TogglePause;
        overlay.MuteClicked += playback.ToggleMute;
        overlay.RewindClicked += playback.Rewind;
        transport.Stats = new StatsOverlay(preview);
        toolbar.Seek.Drive(overlay);
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
        columns.ColumnDefinitions[WideColumn].Width = new GridLength(saved.CanvasWeight, GridUnitType.Star);
        columns.ColumnDefinitions[SideColumn].Width = new GridLength(saved.SideWeight, GridUnitType.Star);
        previewShare = new GridLength(saved.PreviewWeight, GridUnitType.Star);
        if (previewBox is { IsVisible: true }) previewRow.Height = previewShare;
        columns.RowDefinitions[2].Height = new GridLength(saved.InspectorWeight, GridUnitType.Star);
        assistantShare = new GridLength(saved.AssistantWidth, GridUnitType.Pixel);
        if (assistant.IsVisible) assistantColumn.Width = assistantShare;
        toolbar.Assistant.IsChecked = saved.AssistantOpen && toolbar.Assistant.IsEnabled;
        controlsShare = new GridLength(saved.ControlsHeight, GridUnitType.Pixel);
        if (ControlsRow is { } row && knobs.View.IsVisible) row.Height = controlsShare;
        ShowControls(saved.ControlsOpen);
        toolbar.Swap.IsChecked = saved.Swapped && toolbar.Swap.IsEnabled;
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
        var (canvas, side) = Share(Column(WideColumn), Column(SideColumn), WindowLayout.DefaultCanvasWeight + WindowLayout.DefaultSideWeight);
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
        };

        static double Weight(GridLength length) => length.IsStar ? length.Value : 1;
        static (double, double) Share(double a, double b, double total) =>
            a + b > 0 ? (a / (a + b) * total, b / (a + b) * total) : (total / 2, total / 2);
    }
}
