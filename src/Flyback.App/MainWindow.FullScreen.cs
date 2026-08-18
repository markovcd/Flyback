using Avalonia.Controls;

namespace Flyback.App;

/// <summary>
/// The preview taking the whole window, and giving it back.
/// </summary>
/// <remarks>
/// Nothing is reparented. The preview stays exactly where it is in the tree and
/// the shell around it is put away instead — which matters more than it looks
/// like it should: the GPU surface is an <c>OpenGlControlBase</c>, and moving one
/// between parents tears its context down and builds it again. A picture that
/// blinked, or a backend that fell back to the processor, every time somebody
/// wanted a closer look would be a poor trade for a simpler method.
/// </remarks>
public sealed partial class MainWindow
{
    /// <summary>
    /// What each track was set to before the preview took over, in order.
    /// </summary>
    /// <remarks>
    /// The sizes are copied out and written back rather than the definition
    /// collections being swapped for flat ones and swapped back. Handing a grid
    /// the collection it used to have does not put the layout back — the objects
    /// return, and the widths they used to decide do not — so what is saved here
    /// is the numbers, and what is changed is the definitions the grid is already
    /// holding. That is also how a splitter works on them, which is what makes it
    /// the arrangement somebody dragged that comes back rather than the one the
    /// window opened with.
    /// </remarks>
    private (GridLength Size, double Minimum)[]? columnsBefore;
    private (GridLength Size, double Minimum)[]? rightRowsBefore;

    /// <summary>
    /// Whether the window was maximised, or merely open, before it went full
    /// screen — the state Escape has to put back, which is not always Normal.
    /// </summary>
    private WindowState stateBefore;

    /// <summary>Whether the preview currently has the window.</summary>
    private bool previewIsFullScreen;

    /// <summary>
    /// A track of no width at all, for the columns and rows the preview is not in.
    /// </summary>
    /// <remarks>
    /// Hiding a child is not enough on its own: a grid track holds the width it
    /// was given whether or not anything visible is standing in it, so the palette
    /// would leave its 220 pixels behind and the canvas its share of the rest.
    /// <para>
    /// Zeroed rather than removed, because Grid indexes its definitions directly.
    /// A child left pointing at column four of a grid that now has one throws out
    /// of <c>MeasureOverride</c> — a crash on a double-click rather than a layout
    /// that merely looks wrong.
    /// </para>
    /// </remarks>
    private static GridLength None => new(0, GridUnitType.Pixel);

    private static GridLength Everything => new(1, GridUnitType.Star);

    private void ToggleFullScreenPreview() => ShowFullScreenPreview(!previewIsFullScreen);

    /// <summary>Hands the window to the preview, or takes it back.</summary>
    private void ShowFullScreenPreview(bool full)
    {
        if (full == previewIsFullScreen) return;

        // All five arrive together when the layout is built, so this is one
        // question rather than five. Before that there is nothing to show.
        if (columns is null || rightPanel is null || previewBox is null
            || toolbar is null || statusBar is null)
        {
            return;
        }

        previewIsFullScreen = full;

        if (full) Collapse();
        else Restore();

        toolbar.IsVisible = !full;
        statusBar.IsVisible = !full;

        // Everything in these two is visible in the ordinary way of things — the
        // assistant, which is not, hangs off the canvas rather than off either of
        // them — so putting them back is a plain yes rather than a remembered one.
        foreach (var child in columns.Children) child.IsVisible = !full || child == rightPanel;
        foreach (var child in rightPanel.Children) child.IsVisible = !full || child == previewBox;

        void Collapse()
        {
            stateBefore = WindowState;

            columnsBefore = [.. columns.ColumnDefinitions.Select(c => (c.Width, c.MinWidth))];
            rightRowsBefore = [.. rightPanel.RowDefinitions.Select(r => (r.Height, r.MinHeight))];

            // Which track to leave standing is read off the layout rather than
            // written down here, so moving the preview panel cannot leave this
            // collapsing the wrong column.
            var keepColumn = Grid.GetColumn(rightPanel);
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

            for (var i = 0; i < rightPanel.RowDefinitions.Count; i++)
            {
                var row = rightPanel.RowDefinitions[i];

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

            if (rightRowsBefore is { } savedRows)
            {
                for (var i = 0; i < savedRows.Length && i < rightPanel.RowDefinitions.Count; i++)
                {
                    rightPanel.RowDefinitions[i].Height = savedRows[i].Size;
                    rightPanel.RowDefinitions[i].MinHeight = savedRows[i].Minimum;
                }
            }

            WindowState = stateBefore;
        }
    }
}
