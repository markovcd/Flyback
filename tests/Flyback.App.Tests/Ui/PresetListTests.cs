using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The presets are offered as a gallery of tiles: in kind order, a heading over each
/// run of one, each tile carrying the preset's own name, description and picture.
/// </summary>
public class PresetListTests : UiTest
{
    private static MainWindow Open()
    {
        var window = new MainWindow();

        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        return window;
    }

    /// <summary>What holds which preset is on the canvas.</summary>
    private static ComboBox Presets(MainWindow window) =>
        All<ComboBox>(window).Single(box => box.Name == "presets");

    /// <summary>Presses the toolbar's preset button, which puts the gallery up.</summary>
    private static void OpenGallery(MainWindow window)
    {
        All<Button>(window).Single(b => b.Name == "presets-glyph")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        for (var attempt = 0; attempt < 20 && !All<ModalOverlay>(window).Any(); attempt++)
            Dispatcher.UIThread.RunJobs();

        Settle(window);
    }

    private static List<Button> Tiles(MainWindow window) =>
        All<Button>(window).Where(b => b.Name == "tile").ToList();

    private static Button Tile(MainWindow window, string name) =>
        Tiles(window).Single(t => ((PatchPreset)t.Tag!).Name == name);

    /// <summary>
    /// A tile's picture is drawn off the UI thread, so it arrives a moment after the
    /// gallery does.
    /// </summary>
    private static void UntilDrawn(MainWindow window, Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);

        while (!done() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(20);
        }

        Settle(window);
        done().ShouldBeTrue("the thumbnail was never drawn");
    }

    /// <summary>
    /// The words a kind is headed with, which are not the words the enum uses:
    /// nobody opening the list is looking for an Interplay.
    /// </summary>
    private static readonly Dictionary<PresetKind, string> Headings = new()
    {
        [PresetKind.Blank] = "BLANK",
        [PresetKind.Idea] = "ONE IDEA",
        [PresetKind.Interplay] = "SOUND AND PICTURE",
        [PresetKind.Showcase] = "SHOWCASE",
    };

    [AvaloniaFact]
    public void A_heading_stands_over_each_run_of_a_kind()
    {
        var window = Open();

        OpenGallery(window);

        var gallery = All<StackPanel>(window).Single(p => p.Name == "gallery");
        var headed = new List<PresetKind>();

        for (var row = 0; row < gallery.Children.Count; row += 2)
        {
            var heading = gallery.Children[row].ShouldBeOfType<TextBlock>();
            var tiles = gallery.Children[row + 1].ShouldBeOfType<WrapPanel>();

            var kinds = tiles.Children.Select(t => ((PatchPreset)((Button)t).Tag!).Kind).Distinct().ToList();

            kinds.Count.ShouldBe(1, "a run holds one kind");
            heading.Text.ShouldBe(Headings[kinds[0]]);
            headed.ShouldNotContain(kinds[0], "a kind is headed once, where its presets begin");
            headed.Add(kinds[0]);
        }

        headed.ShouldBe([PresetKind.Blank, PresetKind.Idea, PresetKind.Interplay, PresetKind.Showcase]);
    }

    /// <summary>
    /// Every preset the list offers is a tile, saying what the list said: its name
    /// and its one line.
    /// </summary>
    [AvaloniaFact]
    public void Each_preset_is_a_tile_with_its_name_and_description()
    {
        var window = Open();

        OpenGallery(window);

        var offered = Presets(window).ItemsSource!.Cast<PatchPreset>().ToList();
        var tiles = Tiles(window);

        tiles.Select(t => (PatchPreset)t.Tag!).ShouldBe(offered);

        foreach (var tile in tiles)
        {
            var preset = (PatchPreset)tile.Tag!;
            var said = All<TextBlock>(tile).Select(t => t.Text).ToList();

            said.ShouldContain(preset.Name);

            if (preset.Description.Length > 0) said.ShouldContain(preset.Description);
        }
    }

    /// <summary>
    /// Clicking a tile is choosing that preset: the canvas takes it and the gallery
    /// comes down.
    /// </summary>
    [AvaloniaFact]
    public void Clicking_a_tile_puts_that_preset_on_the_canvas()
    {
        var window = Open();
        var editor = All<NodeEditor>(window).Single();
        var before = editor.Patch.Nodes.Select(n => n.Id).ToList();

        OpenGallery(window);

        Tile(window, "Kaleidoscope").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Settle(window);

        (Presets(window).SelectedItem as PatchPreset)!.Name.ShouldBe("Kaleidoscope");
        editor.Patch.Nodes.Select(n => n.Id).ShouldNotBe(before);
        All<ModalOverlay>(window).ShouldBeEmpty("picking one is answering the dialog");
    }

    /// <summary>The one on the canvas is outlined, so the gallery says where somebody is.</summary>
    [AvaloniaFact]
    public void The_preset_on_the_canvas_is_outlined()
    {
        var window = Open();

        OpenGallery(window);

        var showing = ((PatchPreset)Presets(window).SelectedItem!).Name;

        foreach (var tile in Tiles(window))
        {
            var outlined = tile.BorderBrush is ISolidColorBrush { Color.A: > 0 };

            outlined.ShouldBe(((PatchPreset)tile.Tag!).Name == showing);
        }
    }

    /// <summary>A preset that draws is shown by what it draws.</summary>
    [AvaloniaFact]
    public void A_picture_preset_shows_its_own_frame()
    {
        var window = Open();

        OpenGallery(window);

        var image = All<Image>(Tile(window, "Plasma")).Single();

        UntilDrawn(window, () => image.Source is not null);

        var frame = image.Source.ShouldBeAssignableTo<WriteableBitmap>()!;

        frame.PixelSize.Width.ShouldBe(PresetThumbnails.Width);
    }

    /// <summary>
    /// A preset with no picture in it has no frame to show: one that is only heard
    /// shows a speaker, and one with nothing wired yet leaves its tile bare.
    /// </summary>
    [AvaloniaFact]
    public void A_preset_with_no_picture_shows_a_speaker_or_nothing()
    {
        var window = Open();

        OpenGallery(window);

        Control Speaker(string preset) => All<ContentControl>(Tile(window, preset)).Single(c => c.Name == "sound-only");
        string Says(string preset) => All<TextBlock>(Tile(window, preset)).Single(t => t.Parent is Grid).Text ?? "";

        UntilDrawn(window, () => Speaker("Clip").IsVisible);

        ToolTip.GetTip(Speaker("Clip")).ShouldBe("Sound only");
        Says("Clip").ShouldBeEmpty();
        All<Image>(Tile(window, "Clip")).Single().Source.ShouldBeNull();

        // Asked for before Clip, so drawn by now: the thumbnails are taken one at a time.
        Speaker("Empty").IsVisible.ShouldBeFalse();
        Says("Empty").ShouldBeEmpty();
        All<Image>(Tile(window, "Empty")).Single().Source.ShouldBeNull();
    }

    /// <summary>
    /// The blank canvas is the first thing in the list, so that somebody meaning
    /// to build their own patch does not read past thirty of somebody else's —
    /// and it is still not what the window opens on, a program that shipped
    /// patches and showed none of them being one whose presets nobody finds.
    /// </summary>
    [AvaloniaFact]
    public void The_blank_canvas_heads_the_list_and_is_not_what_opens()
    {
        var window = Open();
        var presets = Presets(window);

        presets.ItemsSource!.OfType<PatchPreset>().First().Kind.ShouldBe(PresetKind.Blank);

        (presets.SelectedItem as PatchPreset).ShouldNotBeNull()
            .Kind.ShouldNotBe(PresetKind.Blank, "the window opens on a patch");
    }
    /// <summary>
    /// Resting the pointer on a tile plays the preset's picture on it, frame after
    /// frame, and moving off puts the still back.
    /// </summary>
    [AvaloniaFact]
    public void A_tile_the_pointer_rests_on_plays_its_picture()
    {
        var window = Open();
        OpenGallery(window);

        var tile = Tile(window, "Plasma");
        var image = All<Image>(tile).Single();

        UntilDrawn(window, () => image.Source is not null);
        var still = image.Source;

        var middle = tile.TranslatePoint(new Point(tile.Bounds.Width / 2, 20), window)!.Value;
        window.MouseMove(middle);
        Settle(window);
        tile.IsPointerOver.ShouldBeTrue();

        UntilDrawn(window, () => image.Source != still);
        var playing = image.Source.ShouldBeOfType<WriteableBitmap>();

        window.MouseMove(new Point(1, 1));
        Settle(window);

        image.Source.ShouldBe(still);
        playing.ShouldNotBe(still);
    }
}
