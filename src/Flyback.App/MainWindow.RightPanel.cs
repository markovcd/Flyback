using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Flyback.App.Controls;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Flyback.Plugins;
using Flyback.Plugins.Settings;
using Colors = Flyback.App.Controls.Colors;

namespace Flyback.App;

/// <summary>
/// The column on the right: the preview over the <see cref="Inspector"/>.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// The preview, the splitter under it and the inspector, down one column of
    /// <paramref name="grid"/>, whose three rows are theirs.
    /// </summary>
    private void BuildRightPanel(Grid grid, int column)
    {
        previewBox = new Border
        {
            Background = Brushes.Black,
            Child = preview,
        };

        // Double-click the picture and it takes the window; double-click it or
        // press Escape to put everything back. The gesture every video player
        // already has, on the one control here that is a video.
        previewBox.DoubleTapped += (_, e) =>
        {
            ToggleFullScreenPreview();
            e.Handled = true;
        };

        Grid.SetColumn(previewBox, column);
        Grid.SetRow(previewBox, 0);

        previewRow = grid.RowDefinitions[0];

        var splitter = previewSplitter = new GridSplitter { Background = Brushes.Transparent, Height = 5 };
        Grid.SetColumn(splitter, column);
        Grid.SetRow(splitter, 1);

        // The plate is docked rather than scrolled: what a block is and the buttons
        // that act on it are wanted wherever the reading has been scrolled to.
        var reading = new DockPanel();

        var plateHost = inspector.PlateHost;
        var wash = inspector.Wash;

        DockPanel.SetDock(plateHost, Dock.Top);

        reading.Children.Add(plateHost);
        reading.Children.Add(new ScrollViewer
        {
            Content = inspector.Panel,

            // Explicitly transparent: a theme that gave the scroll viewer a
            // background would paint straight over the wash and the mark.
            Background = Brushes.Transparent,
        });

        // The block's face sits behind the inspector rather than beside it, and
        // never takes a click.
        var inspectorBorder = inspectorBox = new Border
        {
            Background = new SolidColorBrush(Colors.Panel),
            Child = new Panel { Children = { wash, reading } },
        };

        // The mark starts under the plate, whatever height the name and the buttons
        // have left it at.
        // The band and the mark are drawn on the wash, so it is told how deep the
        // name's row is and how far down the plate reaches.
        plateHost.PropertyChanged += (_, e) =>
        {
            if (e.Property != BoundsProperty) return;

            wash.Below = plateHost.Bounds.Height;
            wash.BandHeight = (plateHost.Content as ModulePlate)?.Band ?? 0;
        };
        Grid.SetColumn(inspectorBorder, column);
        Grid.SetRow(inspectorBorder, 2);

        // Over the preview's own cell while it has the window, and nowhere otherwise.
        var overlay = transportOverlay = new TransportOverlay() { IsVisible = false };

        overlay.PauseClicked += TogglePause;
        overlay.MuteClicked += playback.ToggleMute;
        overlay.RewindClicked += RewindToZero;

        grid.Children.Add(previewBox);
        grid.Children.Add(knobs.Stage);
        grid.Children.Add(overlay);
        grid.Children.Add(splitter);
        grid.Children.Add(inspectorBorder);
    }
}
