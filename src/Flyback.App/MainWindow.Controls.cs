using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Flyback.App.Controls;

namespace Flyback.App;

/// <summary>
/// Where the <see cref="PanelKnobs"/> stand: the row under the canvas, the edge
/// above it, and the toolbar button that shows it.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The edge above the panel, dragged to give it more rows or fewer.</summary>
    private readonly GridSplitter controlsSplitter = new()
    {
        Name = "controls-splitter",
        Background = Brushes.Transparent,
        Height = 5,
        IsVisible = false,
    };

    /// <summary>The row the panel stands in, under the canvas or, swapped, under the preview.</summary>
    private RowDefinition? ControlsRow => knobs.View.Parent is Grid grid ? grid.RowDefinitions[2] : null;

    /// <summary>
    /// The panel's height, kept while it is hidden. One row of knobs to start with;
    /// more rows wrap in beneath once it is dragged taller.
    /// </summary>
    private GridLength controlsShare = new(118);

    private void WireControls()
    {
        toolbar.Knobs.IsCheckedChanged += (_, _) => ShowControls(toolbar.Knobs.IsChecked == true);

        knobs.Wanted += (_, _) => ShowControls(true);

        // A socket's own knob on the canvas: heard as it turns, written into the
        // text and the panel when the hand comes off it.
        editor.InputTurned += (_, pick) => document.Turned(pick.Node, pick.Port);
        editor.InputLetGo += (_, pick) =>
        {
            document.HandCameOff();
            if (editor.SelectedNode?.Id == pick.Node || editor.SelectedGroup?.Members.Contains(pick.Node) == true) inspector.Build();
        };
    }

    /// <summary>Shows or hides the panel, keeping the toolbar button in step.</summary>
    private void ShowControls(bool shown)
    {
        // The full screen preview owns every row, the knobs' too while swapped.
        if (previewIsFullScreen) return;

        var panel = knobs.View;

        // Only on a change: showing a panel already shown would put back the height
        // it had when last hidden, over whatever it has been dragged to since.
        if (ControlsRow is { } controlsRow && shown != panel.IsVisible)
        {
            // A pixel row rather than an auto one, so the splitter has a height to
            // change; zeroed while hidden, with its minimum, the way the assistant's is.
            if (!shown && panel.IsVisible) controlsShare = controlsRow.Height;

            controlsRow.MinHeight = shown ? 60d : 0d;
            controlsRow.Height = shown ? controlsShare : new GridLength(0);
        }

        panel.IsVisible = shown;
        controlsSplitter.IsVisible = shown;

        if (toolbar.Knobs.IsChecked != shown) toolbar.Knobs.IsChecked = shown;

        if (!shown) knobs.Link(null);
    }
}
