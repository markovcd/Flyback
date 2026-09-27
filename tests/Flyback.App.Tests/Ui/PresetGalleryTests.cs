using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Flyback.App.Gallery;
using Flyback.Core.Graph;
using Microsoft.Extensions.DependencyInjection;
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
        var container = Container();
        var parts = container.GetRequiredService<PresetGallery>().Build(
            [.. Presets.All.OrderBy(preset => preset.Kind)],
            showing: null,
            pointedAt: reported.Add);
        var tiles = parts.Tiles(_ => { }, _ => { });
        var window = Show(tiles, width: 900);
        Attach(container, window);
        return (window, tiles, reported);
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

    [AvaloniaFact]
    public void A_picked_preset_is_sent_to_the_answer_action()
    {
        PatchPreset? picked = null;
        var container = Container();

        var parts = container.GetRequiredService<PresetGallery>().Build(
            [.. Presets.All.OrderBy(preset => preset.Kind)],
            showing: null);
        var tiles = parts.Tiles(preset => picked = preset, _ => { });
        var window = Show(tiles, width: 900);
        Attach(container, window);
        Settle(window);

        Press(Tile(tiles, "Plasma"));

        picked?.Name.ShouldBe("Plasma");
    }

    /// <summary>A gallery of hundreds draws the tiles on screen, and one further down once it is scrolled to.</summary>
    [AvaloniaFact]
    public void Only_the_tiles_in_sight_are_drawn()
    {
        var container = Container();
        var thumbnails = container.GetRequiredService<PresetThumbnails>();
        var ordered = Presets.All.OrderBy(preset => preset.Kind).ToList();
        var parts = container.GetRequiredService<PresetGallery>().Build(ordered, showing: null);
        var tiles = parts.Tiles(_ => { }, _ => { });
        var window = Show(new ScrollViewer { Height = 400, Content = tiles }, width: 900);
        Attach(container, window);
        thumbnails.IsAsked(ordered[0]).ShouldBeTrue();
        thumbnails.IsAsked(ordered[^1]).ShouldBeFalse();

        Tile(tiles, ordered[^1].Name).BringIntoView();
        Settle(window);

        thumbnails.IsAsked(ordered[^1]).ShouldBeTrue();
    }
}
