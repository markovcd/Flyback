using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The gallery reporting which tile the pointer is on, which is the whole of what
/// it says about auditioning: what the window then does with that is its own.
/// </summary>
public class PresetGalleryTests : UiTest
{
    private (Window Window, Control Tiles, List<PointedTile?> Reported) Gallery()
    {
        var reported = new List<PointedTile?>();

        var parts = PresetGallery.Build(
            [.. Presets.All.OrderBy(preset => preset.Kind)],
            showing: null,
            new PresetThumbnails(NodeCatalog.BuiltIn),
            pointedAt: reported.Add);

        return (Show(parts.Tiles, width: 900), parts.Tiles, reported);
    }

    private static Button Tile(Control tiles, string name) =>
        All<Button>(tiles).Single(button => button.Name == "tile" && ((PatchPreset)button.Tag!).Name == name);

    /// <summary>Puts the pointer on a control, or on none of them for null.</summary>
    private static void Point(Window window, Control? onto)
    {
        var at = onto is null
            ? new Point(1, 1)
            : onto.TranslatePoint(new Point(onto.Bounds.Width / 2, 20), window)!.Value;

        window.MouseMove(at);
        Settle(window);
    }

    [AvaloniaFact]
    public void A_tile_the_pointer_enters_is_the_one_reported()
    {
        var (window, tiles, reported) = Gallery();

        Point(window, Tile(tiles, "Plasma"));

        reported.ShouldHaveSingleItem()!.Preset.Name.ShouldBe("Plasma");
    }

    [AvaloniaFact]
    public void Leaving_a_tile_reports_that_the_pointer_is_on_none()
    {
        var (window, tiles, reported) = Gallery();

        Point(window, Tile(tiles, "Plasma"));
        Point(window, null);

        reported.Count.ShouldBe(2);
        reported[^1].ShouldBeNull();
    }

    /// <summary>
    /// The picture the tile hands over is the one a preset is then played on, so it
    /// has to be that tile's own.
    /// </summary>
    [AvaloniaFact]
    public void The_reported_tile_carries_its_own_picture()
    {
        var (window, tiles, reported) = Gallery();
        var tile = Tile(tiles, "Plasma");

        Point(window, tile);

        reported.ShouldHaveSingleItem()!.Picture.ShouldBe(All<Image>(tile).Single());
    }
}
