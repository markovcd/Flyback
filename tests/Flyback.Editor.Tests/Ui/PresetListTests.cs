using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Flyback.Editor.Controls;
using Flyback.Engine.Graph;
using Flyback.Ui.Controls;
using Flyback.Core.Graph;
using Shouldly;
using Flyback.Editor.Canvas;
using Flyback.Editor.Gallery;
using Flyback.Editor.Windows;

namespace Flyback.Editor.Tests.Ui;

/// <summary>
/// The presets are offered as a gallery of tiles: in kind order, a heading over each
/// run of one, each tile carrying the preset's own name, description and picture.
/// </summary>
public class PresetListTests : EditorTest
{

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

    /// <summary>The tile of the preset called <paramref name="name"/>, scrolled into view, since a tile draws once seen.</summary>
    private static Button Tile(MainWindow window, string name)
    {
        var tile = Tiles(window).Single(t => ((PatchPreset)t.Tag!).Name == name);

        tile.BringIntoView();
        Settle(window);

        return tile;
    }

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
            var heading = Title(gallery.Children[row]);
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

    /// <summary>The words over a run of tiles.</summary>
    private static TextBlock Title(Control heading) =>
        heading.ShouldBeOfType<Grid>().Children.OfType<TextBlock>().Single(t => t.Name == "run-title");

    /// <summary>The run titles still showing.</summary>
    private static List<string?> ShownHeadings(MainWindow window) =>
        [.. All<StackPanel>(window).Single(p => p.Name == "gallery").Children
            .Where(c => c.Name == "run-heading" && c.IsVisible)
            .Select(c => Title(c).Text)];

    private static string? Described(MainWindow window) => All<TextBlock>(window).Single(t => t.Name == "detail-name").Text;

    /// <summary>
    /// Clicking a tile chooses it: the column beside says what it is, and the
    /// canvas keeps what it had until it is opened.
    /// </summary>
    [AvaloniaFact]
    public void Clicking_a_tile_chooses_it_and_says_what_it_is()
    {
        var window = Open();
        var editor = All<NodeEditor>(window).Single();
        var before = editor.History.Patch.Nodes.Select(n => n.Id).ToList();

        OpenGallery(window);

        Tile(window, "Kaleidoscope").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Settle(window);

        Described(window).ShouldBe("Kaleidoscope");
        editor.History.Patch.Nodes.Select(n => n.Id).ShouldBe(before);
        All<ModalOverlay>(window).ShouldHaveSingleItem("choosing is not answering the dialog");
    }

    /// <summary>The button under the chosen tile's description opens it: the canvas takes it and the gallery comes down.</summary>
    [AvaloniaFact]
    public void Using_the_chosen_tile_puts_that_preset_on_the_canvas()
    {
        var window = Open();
        var editor = All<NodeEditor>(window).Single();
        var before = editor.History.Patch.Nodes.Select(n => n.Id).ToList();

        OpenGallery(window);

        Tile(window, "Kaleidoscope").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        All<Button>(window).Single(b => b.Name == "use-preset").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Settle(window);

        (Presets(window).SelectedItem as PatchPreset)!.Name.ShouldBe("Kaleidoscope");
        editor.History.Patch.Nodes.Select(n => n.Id).ShouldNotBe(before);
        All<ModalOverlay>(window).ShouldBeEmpty("opening one is answering the dialog");
    }

    [AvaloniaFact]
    public void Double_clicking_a_tile_puts_that_preset_on_the_canvas()
    {
        var window = Open();

        OpenGallery(window);

        Tile(window, "Kaleidoscope").RaiseEvent(new TappedEventArgs(InputElement.DoubleTappedEvent, null!));
        Settle(window);

        (Presets(window).SelectedItem as PatchPreset)!.Name.ShouldBe("Kaleidoscope");
        All<ModalOverlay>(window).ShouldBeEmpty();
    }

    private static TextBox Filter(MainWindow window) =>
        All<TextBox>(window).Single(box => box.Name == "preset-filter");

    private static List<PatchPreset> Showing(MainWindow window) =>
        [.. Tiles(window).Where(t => t.IsEffectivelyVisible).Select(t => (PatchPreset)t.Tag!)];

    private static void PressKey(InputElement target, Key key) =>
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, Source = target });

    /// <summary>
    /// The gallery opens with the keyboard in its filter, so typing narrows it at
    /// once, as the module list does.
    /// </summary>
    [AvaloniaFact]
    public void The_filter_has_the_keyboard_when_the_gallery_opens()
    {
        var window = Open();

        OpenGallery(window);

        Filter(window).IsFocused.ShouldBeTrue();
    }

    /// <summary>
    /// Typing leaves the presets whose name has it, and takes away every heading
    /// with nothing left under it. Enter picks the first of what is left.
    /// </summary>
    [AvaloniaFact]
    public void Typing_narrows_to_the_names_that_match_and_Enter_picks_the_first()
    {
        var window = Open();

        OpenGallery(window);

        var filter = Filter(window);
        filter.Text = "kaleido";
        Settle(window);

        Showing(window).Select(p => p.Name).ShouldBe(["Kaleidoscope"]);

        ShownHeadings(window).ShouldBe(["ONE IDEA"]);

        PressKey(filter, Key.Enter);
        Settle(window);

        (Presets(window).SelectedItem as PatchPreset)!.Name.ShouldBe("Kaleidoscope");
        All<ModalOverlay>(window).ShouldBeEmpty();
    }

    /// <summary>The frame stays the size it opened at however few tiles the filter leaves.</summary>
    [AvaloniaFact]
    public void Typing_does_not_resize_the_gallery()
    {
        var window = Open();

        OpenGallery(window);

        var frame = All<Border>(window).Single(b => b.Name == "dialog");
        var opened = frame.Bounds.Size;

        Filter(window).Text = "kaleido";
        Settle(window);

        frame.Bounds.Size.ShouldBe(opened);

        Filter(window).Text = "zzzz";
        Settle(window);

        frame.Bounds.Size.ShouldBe(opened);
    }

    /// <summary>A heading's own words match everything under it, as a category does in the module list.</summary>
    [AvaloniaFact]
    public void Typing_a_heading_keeps_its_whole_run()
    {
        var window = Open();

        OpenGallery(window);

        Filter(window).Text = "showcase";
        Settle(window);

        var showcases = Presets(window).ItemsSource!.Cast<PatchPreset>().Where(p => p.Kind == PresetKind.Showcase);

        Showing(window).ShouldBe(showcases);
    }

    /// <summary>The arrows walk what is left, and Enter picks where they stopped.</summary>
    [AvaloniaFact]
    public void The_arrows_walk_the_matches()
    {
        var window = Open();

        OpenGallery(window);

        var filter = Filter(window);
        filter.Text = "a";
        Settle(window);

        var matches = Showing(window);
        matches.Count.ShouldBeGreaterThan(2);

        PressKey(filter, Key.Down);
        PressKey(filter, Key.Down);
        PressKey(filter, Key.Up);
        PressKey(filter, Key.Enter);
        Settle(window);

        (Presets(window).SelectedItem as PatchPreset).ShouldBe(matches[1]);
    }

    /// <summary>
    /// Nothing matching says so. Escape empties the box, and on an empty box closes
    /// the gallery.
    /// </summary>
    [AvaloniaFact]
    public void Escape_empties_the_filter_before_it_closes_the_gallery()
    {
        var window = Open();

        OpenGallery(window);

        var filter = Filter(window);
        filter.Text = "zzzz";
        Settle(window);

        Showing(window).ShouldBeEmpty();
        All<TextBlock>(window).ShouldContain(t => t.IsEffectivelyVisible && t.Text == "Nothing matches “zzzz”.");

        filter.Focus();
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Settle(window);

        filter.Text.ShouldBeEmpty();
        Showing(window).Count.ShouldBe(Tiles(window).Count);
        All<ModalOverlay>(window).ShouldNotBeEmpty();

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Settle(window);

        All<ModalOverlay>(window).ShouldBeEmpty();
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
    /// The pointer coming to rest on a tile, and leaving it, which is what the
    /// window listens to in order to try the preset.
    /// </summary>
    /// <remarks>
    /// What the trying itself looks like is two tests of its own: PresetGalleryTests
    /// for the tile reporting the pointer, and PresetMotionTests for the picture that
    /// then plays. Driving the whole of it from here meant waiting on a one-second
    /// dwell, a compile and a sound device through the one thread headless gives
    /// every test in the assembly.
    /// </remarks>
    [AvaloniaFact]
    public void A_tile_takes_the_pointer_coming_to_rest_on_it()
    {
        var window = Open();
        OpenGallery(window);

        var tile = Tile(window, "Plasma");
        var image = All<Image>(tile).Single();

        UntilDrawn(window, () => image.Source is not null);

        var middle = tile.TranslatePoint(new Point(tile.Bounds.Width / 2, 20), window)!.Value;

        window.MouseMove(middle);
        Settle(window);

        tile.IsPointerOver.ShouldBeTrue();

        window.MouseMove(new Point(1, 1));
        Settle(window);

        tile.IsPointerOver.ShouldBeFalse();
    }
}
