using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Flyback.Ui.Controls;

namespace Flyback.Editor.Inspect;

/// <summary>
/// Everything that can be done to the selected block, each a glyph with its word under it,
/// opened from the folded header: the block's name and what it is, then what acts on it, then
/// what takes the selection elsewhere.
/// </summary>
/// <remarks>
/// Words rather than tips, because a finger has no hover; the tiles press the plate's own
/// buttons, so what is offered and what pressing it does are decided once, there.
/// </remarks>
internal sealed class PlateMenu
{
    private const double Width = 288;
    private const double TileHeight = 56;

    private readonly ModulePlate plate;
    private readonly Action rename;

    /// <param name="rename">Puts a box where the header's name is.</param>
    public PlateMenu(ModulePlate plate, Action rename)
    {
        this.plate = plate;
        this.rename = rename;
    }

    /// <summary>Opens the menu over <paramref name="from"/>, above it where there is room.</summary>
    public void Open(Control from)
    {
        var flyout = new Flyout { Placement = PlacementMode.TopEdgeAlignedLeft };

        flyout.Content = Content(close: flyout.Hide);
        flyout.ShowAt(from);
    }

    /// <summary>What the menu holds, closed by <paramref name="close"/> once a tile has done its work.</summary>
    internal Control Content(Action close)
    {
        var body = new StackPanel { Name = "plate-menu", Width = Width, Spacing = 8 };

        if (plate.Face is { } face)
        {
            body.Children.Add(Heading(face));

            if (face.Description is { Length: > 0 } description)
                body.Children.Add(new TextBlock
                {
                    Text = description,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = Text.Body,
                    Foreground = Text.Muted,
                });
        }

        var rows = PlateActions.Rows(plate);

        for (var r = 0; r < rows.Count; r++)
        {
            var tiles = new UniformGrid { Columns = 4, ColumnSpacing = 6, RowSpacing = 6 };

            foreach (var mirrored in rows[r])
            {
                tiles.Children.Add(Tile(mirrored, close));

                // Renaming follows switching, as the first thing a block is told after whether it plays.
                if (r == 0 && tiles.Children.Count == 1 && plate.Face?.Naming is not null) tiles.Children.Add(RenameTile(close));
            }

            if (r == 0 && tiles.Children.Count == 0 && plate.Face?.Naming is not null) tiles.Children.Add(RenameTile(close));

            body.Children.Add(Section(r == 0 ? "Module" : "Selection"));
            body.Children.Add(tiles);
        }

        return body;
    }

    private static Control Heading(PlateFace face)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

        if (face.Glyph is { } glyph && face.MarkInk is { } ink)
            row.Children.Add(new Avalonia.Controls.Shapes.Path
            {
                Data = glyph,
                Stroke = ink,
                StrokeThickness = 2.2,
                StrokeLineCap = PenLineCap.Round,
                Stretch = Stretch.Uniform,
                Width = 20,
                Height = 20,
                VerticalAlignment = VerticalAlignment.Center,
            });

        row.Children.Add(new TextBlock { Text = face.Title, FontSize = Text.Heading, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = face.Kind, FontSize = Text.Small, Foreground = Text.Muted, VerticalAlignment = VerticalAlignment.Center });

        return row;
    }

    private static TextBlock Section(string title) => new()
    {
        Text = title.ToUpperInvariant(),
        FontSize = Text.Small,
        FontWeight = FontWeight.SemiBold,
        Foreground = Text.Muted,
        Margin = new Thickness(0, 2, 0, 0),
    };

    private static Button Tile(Button mirrored, Action close)
    {
        var said = PlateActions.Of(mirrored.Name)!.Value;
        var tile = PlateActions.Worded("menu-" + mirrored.Name, said.Glyph(), said.Label, ToolTip.GetTip(mirrored) as string, TileHeight);

        tile.IsEnabled = mirrored.IsEnabled;
        tile.Click += (_, _) =>
        {
            close();
            PlateActions.Press(mirrored);
        };

        return tile;
    }

    private Button RenameTile(Action close)
    {
        var tile = PlateActions.Worded("menu-rename", Glyphs.Pencil(), "Rename", "Give it a name of its own", TileHeight);

        tile.Click += (_, _) =>
        {
            close();
            rename();
        };

        return tile;
    }
}
