using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The patch on the canvas saved as a preset from the gallery, which then lists it
/// under a heading of its own after every preset the program offers.
/// </summary>
public class SavedPresetTests : UiTest, IDisposable
{
    private readonly string folder = Path.Combine(
        Path.GetTempPath(),
        "flyback-presets-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);

        GC.SuppressFinalize(this);
    }

    private MainWindow Open()
    {
        var window = new MainWindow(presetFolder: folder);

        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        return window;
    }

    private static ComboBox Presets(MainWindow window) =>
        All<ComboBox>(window).Single(box => box.Name == "presets");

    private static void OpenGallery(MainWindow window)
    {
        All<Button>(window).Single(b => b.Name == "presets-glyph")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        for (var attempt = 0; attempt < 20 && !All<ModalOverlay>(window).Any(); attempt++)
            Dispatcher.UIThread.RunJobs();

        Settle(window);
    }

    private static void Click(Button button, MainWindow window)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Settle(window);
    }

    private static WrapPanel Yours(MainWindow window) =>
        All<WrapPanel>(window).Single(p => p.Name == "yours");

    private static List<PatchPreset> SavedTiles(MainWindow window) =>
        [.. All<Button>(Yours(window)).Where(b => b.Name == "tile").Select(b => (PatchPreset)b.Tag!)];

    /// <summary>Saves the patch on the canvas under <paramref name="name"/>, the way a person would.</summary>
    private static void SaveAs(MainWindow window, string name)
    {
        Click(All<Button>(window).Single(b => b.Name == "keep-preset"), window);

        All<TextBox>(window).Single(b => b.Name == "preset-name").Text = name;
        Settle(window);

        Click(All<Button>(window).Single(b => b.Name == "save-preset"), window);
    }

    [AvaloniaFact]
    public void The_saved_run_comes_last_with_a_way_to_save_one()
    {
        var window = Open();

        OpenGallery(window);

        var gallery = All<StackPanel>(window).Single(p => p.Name == "gallery");

        gallery.Children[^2].ShouldBeOfType<TextBlock>().Text.ShouldBe(PresetGallery.YoursHeading);
        gallery.Children[^1].ShouldBeSameAs(Yours(window));

        SavedTiles(window).ShouldBeEmpty("nothing has been saved yet");
        All<Button>(Yours(window)).ShouldContain(b => b.Name == "keep-preset");
    }

    [AvaloniaFact]
    public void A_patch_saved_as_a_preset_is_a_tile_and_a_file()
    {
        var window = Open();
        var offered = Presets(window).ItemsSource!.Cast<PatchPreset>().Count();

        OpenGallery(window);
        SaveAs(window, "Mine");

        SavedTiles(window).Select(p => p.Name).ShouldBe(["Mine"]);
        File.Exists(Path.Combine(folder, "Mine" + PatchBundle.Extension)).ShouldBeTrue();

        var listed = Presets(window).ItemsSource!.Cast<PatchPreset>().ToList();

        listed.Count.ShouldBe(offered + 1);
        listed[^1].Name.ShouldBe("Mine", "a saved preset comes after every other");
    }

    [AvaloniaFact]
    public void Clicking_a_saved_preset_puts_it_on_the_canvas()
    {
        var window = Open();
        var editor = All<NodeEditor>(window).Single();
        var saved = editor.Patch.Nodes.Select(n => n.TypeId).Order().ToList();

        OpenGallery(window);
        SaveAs(window, "Mine");

        // Somewhere else first, so arriving back is visibly the saved one.
        Click(All<Button>(window).Single(b => b.Name == "tile" && ((PatchPreset)b.Tag!).Name == "Empty"), window);
        editor.Patch.Nodes.Count.ShouldBe(1);

        OpenGallery(window);
        Click(All<Button>(Yours(window)).Single(b => b.Name == "tile"), window);

        (Presets(window).SelectedItem as PatchPreset)!.Name.ShouldBe("Mine");
        editor.Patch.Nodes.Select(n => n.TypeId).Order().ShouldBe(saved);
    }

    [AvaloniaFact]
    public void A_built_in_name_is_refused()
    {
        var window = Open();

        OpenGallery(window);

        Click(All<Button>(window).Single(b => b.Name == "keep-preset"), window);

        All<TextBox>(window).Single(b => b.Name == "preset-name").Text = "Plasma";
        Settle(window);

        All<Button>(window).Single(b => b.Name == "save-preset").IsEnabled.ShouldBeFalse();
    }

    /// <summary>Saves "Mine", then asks to delete it, and returns its tile with the question up.</summary>
    private static Button AskToDelete(MainWindow window)
    {
        OpenGallery(window);
        SaveAs(window, "Mine");

        var tile = All<Button>(Yours(window)).Single(b => b.Name == "tile");

        ((MenuFlyout)tile.ContextFlyout!).Items.OfType<MenuItem>().Single()
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Settle(window);

        return tile;
    }

    private static Button Answer(Button tile, string glyph) =>
        All<Button>(tile).Single(b => b.Content as string == glyph);

    [AvaloniaFact]
    public void A_tile_being_asked_about_deleting_does_not_open()
    {
        var window = Open();
        var tile = AskToDelete(window);
        var showing = Presets(window).SelectedItem;

        Click(tile, window);

        All<ModalOverlay>(window).ShouldNotBeEmpty("the gallery is still up");
        Presets(window).SelectedItem.ShouldBeSameAs(showing);
    }

    [AvaloniaFact]
    public void Keeping_a_preset_that_was_asked_about_does_not_open_it()
    {
        var window = Open();
        var tile = AskToDelete(window);
        var showing = Presets(window).SelectedItem;

        Click(Answer(tile, "✕"), window);

        All<ModalOverlay>(window).ShouldNotBeEmpty("the gallery is still up");
        Presets(window).SelectedItem.ShouldBeSameAs(showing);
        SavedTiles(window).Select(p => p.Name).ShouldBe(["Mine"]);

        // The question is gone, so the tile is an ordinary one again.
        Click(All<Button>(Yours(window)).Single(b => b.Name == "tile"), window);

        All<ModalOverlay>(window).ShouldBeEmpty();
        (Presets(window).SelectedItem as PatchPreset)!.Name.ShouldBe("Mine");
    }

    [AvaloniaFact]
    public void Deleting_a_preset_does_not_open_it_either()
    {
        var window = Open();
        var tile = AskToDelete(window);
        var showing = Presets(window).SelectedItem;

        Click(Answer(tile, "✔"), window);

        All<ModalOverlay>(window).ShouldNotBeEmpty("the gallery is still up");
        Presets(window).SelectedItem.ShouldBeSameAs(showing);
        SavedTiles(window).ShouldBeEmpty();
        File.Exists(Path.Combine(folder, "Mine" + PatchBundle.Extension)).ShouldBeFalse();
    }

    /// <summary>
    /// Windows hands a box its character only for a key press nobody handled, and a
    /// text box leaves a letter unhandled, so the dialog around it must too.
    /// </summary>
    [AvaloniaFact]
    public void A_letter_typed_into_the_name_reaches_the_window_unhandled()
    {
        var window = Open();

        OpenGallery(window);
        Click(All<Button>(window).Single(b => b.Name == "keep-preset"), window);

        var name = All<TextBox>(window).Single(b => b.Name == "preset-name");
        var handled = new List<bool>();

        name.Focus();
        window.AddHandler(InputElement.KeyDownEvent, (_, e) => handled.Add(e.Handled), handledEventsToo: true);

        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None);

        handled.ShouldBe([false]);
    }

    [AvaloniaFact]
    public void A_window_given_no_folder_has_no_saved_run()
    {
        var window = new MainWindow();

        window.Show();
        Settle(window);
        OpenGallery(window);

        All<WrapPanel>(window).ShouldNotContain(p => p.Name == "yours");
    }
}
