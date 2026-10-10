using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Flyback.Editor.Gallery;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Flyback.Editor.Tests.Gallery;

/// <summary>
/// The gallery reporting which tile the pointer is on, which is the whole of what
/// it says about auditioning: what the window then does with that is its own.
/// </summary>
public class PresetGalleryTests : EditorTest
{
    private (Window Window, Control Tiles, List<PointedTile?> Reported) Gallery()
    {
        var reported = new List<PointedTile?>();
        var container = Container();
        var parts = container.GetRequiredService<PresetGallery>().Build(
            [.. Presets.All.OrderBy(preset => preset.Kind)],
            showing: null,
            pointedAt: reported.Add);
        var tiles = parts.Tiles(_ => { });
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
        IPreset? picked = null;
        var container = Container();

        var parts = container.GetRequiredService<PresetGallery>().Build(
            [.. Presets.All.OrderBy(preset => preset.Kind)],
            showing: null);
        var tiles = parts.Tiles(preset => picked = preset);
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
        var tiles = parts.Tiles(_ => { });
        var window = Show(new ScrollViewer { Height = 400, Content = tiles }, width: 900);
        Attach(container, window);
        thumbnails.IsAsked(ordered[0]).ShouldBeTrue();
        thumbnails.IsAsked(ordered[^1]).ShouldBeFalse();

        Tile(tiles, ordered[^1].Name).BringIntoView();
        Settle(window);

        thumbnails.IsAsked(ordered[^1]).ShouldBeTrue();
    }

    [AvaloniaFact]
    public void The_preset_on_the_canvas_is_scrolled_into_sight_when_the_gallery_opens()
    {
        var container = Container();
        var ordered = Presets.All.OrderBy(preset => preset.Kind).ToList();
        var open = ordered[^1];
        var parts = container.GetRequiredService<PresetGallery>().Build(ordered, showing: open);
        var window = Show(parts.Tiles(_ => { }), width: 1100);
        Attach(container, window);
        var scroll = All<ScrollViewer>(window).Single(s => s.Name == "gallery-scroll");
        var tile = All<Button>(window).Single(b => b.Name == "tile" && ((PatchPreset)b.Tag!).Name == open.Name);

        Pump(() => scroll.Offset.Y > 0, window);

        var top = tile.TranslatePoint(new Point(0, 0), scroll)!.Value.Y;

        top.ShouldBeGreaterThanOrEqualTo(0);
        (top + tile.Bounds.Height).ShouldBeLessThanOrEqualTo(scroll.Bounds.Height);
    }

    private (Window Window, Control Picker) Picker(double width = 1100)
    {
        var container = Container();
        var parts = container.GetRequiredService<PresetGallery>().Build(
            [.. Presets.All.OrderBy(preset => preset.Kind)],
            showing: null);
        var picker = parts.Tiles(_ => { });
        var window = Show(picker, width);
        Attach(container, window);
        return (window, picker);
    }

    private static List<PatchPreset> Shown(Control picker) =>
        [.. All<Button>(picker).Where(b => b is { Name: "tile" } && b.IsEffectivelyVisible).Select(b => (PatchPreset)b.Tag!)];

    private static Button Row(Control picker, string label) =>
        All<Button>(picker).Single(b => b.Name == "filter-row" && (string)b.Tag! == label);

    /// <summary>Waits until every tile has said which halves of the Output it works with.</summary>
    private static void UntilEveryTileSaid(Window window, Control picker) =>
        Pump(() => All<Button>(picker).Where(b => b.Name == "tile")
            .All(tile => All<StackPanel>(tile).Single(p => p.Name == "badges").Children.Count > 0), window);

    [AvaloniaFact]
    public void A_section_shows_only_its_own_run()
    {
        var (window, picker) = Picker();

        Press(Row(picker, "One idea"));
        Settle(window);

        Shown(picker).ShouldNotBeEmpty();
        Shown(picker).ShouldAllBe(preset => preset.Kind == PresetKind.Idea);
        All<TextBlock>(picker).Single(t => t.Name == "preset-tally").Text
            .ShouldBe($"{Shown(picker).Count} of {Presets.All.Count} presets");

        Press(All<Button>(picker).Single(b => b.Name == "clear-filters"));
        Settle(window);

        Shown(picker).Count.ShouldBe(Presets.All.Count);
    }

    [AvaloniaFact]
    public void Works_with_sound_leaves_only_the_presets_that_are_heard()
    {
        var (window, picker) = Picker();

        UntilEveryTileSaid(window, picker);
        Press(Row(picker, "Sound"));
        Settle(window);

        var heard = Presets.All
            .Where(preset => preset.Build(NodeCatalog.BuiltIn).Reaches() is var (picture, sound) && (sound || !picture))
            .Select(preset => preset.Name);

        Shown(picker).Select(preset => preset.Name).ShouldBe(heard, ignoreOrder: true);
        Shown(picker).Count.ShouldBeLessThan(Presets.All.Count);
    }

    [AvaloniaFact]
    public void A_topic_leaves_only_the_presets_tagged_with_it()
    {
        var (window, picker) = Picker();

        UntilEveryTileSaid(window, picker);

        var chip = All<Button>(All<WrapPanel>(picker).Single(p => p.Name == "topics")).First(b => b.Name == "topic");
        var topic = (string)chip.Tag!;

        Press(chip);
        Settle(window);

        var tagged = Presets.All
            .Where(preset => preset.Build(NodeCatalog.BuiltIn).Tags?.Contains(topic, StringComparer.OrdinalIgnoreCase) ?? false)
            .Select(preset => preset.Name);

        Shown(picker).Select(preset => preset.Name).ShouldBe(tagged, ignoreOrder: true);
    }

    [AvaloniaFact]
    public void The_chosen_tile_is_described_beside_the_cards()
    {
        var (window, picker) = Picker();

        Press(Tile(picker, "Plasma"));
        Settle(window);

        All<TextBlock>(picker).Single(t => t.Name == "detail-name").Text.ShouldBe("Plasma");
        All<TextBlock>(picker).Single(t => t.Name == "detail-section").Text.ShouldBe("One idea");
        All<Button>(picker).Single(b => b.Name == "use-preset").IsEnabled.ShouldBeTrue();
    }

    /// <summary>Too narrow for three columns, the filter goes above the cards and the chosen one's button below.</summary>
    [AvaloniaFact]
    public void A_narrow_gallery_puts_the_filter_above_and_the_button_below()
    {
        var (_, picker) = Picker(width: 460);

        All<Border>(picker).Single(b => b.Name == "preset-side").IsVisible.ShouldBeFalse();
        All<Border>(picker).Single(b => b.Name == "preset-detail").IsVisible.ShouldBeFalse();

        var bar = All<Border>(picker).Single(b => b.Name == "preset-bar");
        var foot = All<Border>(picker).Single(b => b.Name == "preset-foot");

        All<TextBox>(bar).ShouldContain(box => box.Name == "preset-filter");
        All<Button>(bar).Count(b => b.Name == "kind-chip").ShouldBe(3);
        All<Button>(foot).ShouldContain(b => b.Name == "use-preset");
    }
}
