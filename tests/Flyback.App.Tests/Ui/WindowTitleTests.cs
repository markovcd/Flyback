using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Flyback.App.Controls;
using Flyback.Core;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// What the title bar says: which patch is on the canvas, and whether there is
/// anything in it to lose.
/// </summary>
/// <remarks>
/// A patch arrives one of three ways — a file opened, a file written, a preset
/// picked — and every one of them arrives with something to call it. What is
/// checked here is the preset, because the other two are behind a file picker
/// the headless platform does not put up; the name is written down in one place
/// for all three, and the save dialog offers it back as the file name.
/// </remarks>
public class WindowTitleTests : UiTest
{
    private const string Program = GlobalConstants.ApplicationName;

    private static MainWindow Open()
    {
        var window = new MainWindow();

        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        return window;
    }

    /// <summary>The toolbar's list of patches to start from.</summary>
    private static ComboBox PresetList(MainWindow window) => All<ComboBox>(window)
        .First(box => box.ItemsSource?.Cast<object>().Any(item => item as string == "Plasma") == true);

    private static NodeEditor Editor(MainWindow window) => All<NodeEditor>(window).Single();

    [AvaloniaFact]
    public void The_title_names_the_patch_the_window_opened_on()
    {
        var window = Open();

        // Whatever the list opens on, which is the patch that was built.
        var opening = PresetList(window).SelectedItem as string;

        opening.ShouldBe(Presets.All[0].Name);
        window.Title.ShouldBe($"{opening} — {Program}");
    }

    [AvaloniaFact]
    public void Picking_a_preset_puts_its_name_in_the_title()
    {
        var window = Open();
        var presets = PresetList(window);

        presets.SelectedIndex = 3;
        Settle(window);

        var picked = presets.SelectedItem as string;

        picked.ShouldNotBe(Presets.All[0].Name, "a different patch is on the canvas");
        window.Title.ShouldBe($"{picked} — {Program}");
    }

    /// <summary>
    /// The dot the title already carried, now after the name rather than instead
    /// of one. It is the only thing in the title that is about the work rather
    /// than about which patch this is.
    /// </summary>
    [AvaloniaFact]
    public void An_edit_marks_the_title_and_undoing_it_takes_the_mark_off()
    {
        var window = Open();
        var editor = Editor(window);

        var named = window.Title.ShouldNotBeNull();

        named.ShouldNotEndWith("•");

        editor.AddNode("value").ShouldNotBeNull();
        Settle(window);

        window.Title.ShouldBe(named + " •");

        editor.Undo().ShouldBeTrue();
        Settle(window);

        window.Title.ShouldBe(named, "back to what was last saved, and still named");
    }

    /// <summary>
    /// Picking a preset over an edited patch renames the title, rather than
    /// leaving it saying what the window used to hold.
    /// </summary>
    [AvaloniaFact]
    public void The_name_follows_the_patch_rather_than_the_window()
    {
        var window = Open();
        var presets = PresetList(window);

        presets.SelectedIndex = 2;
        Settle(window);

        var first = window.Title;

        presets.SelectedIndex = 5;
        Settle(window);

        window.Title.ShouldNotBe(first);
        window.Title.ShouldBe($"{presets.SelectedItem as string} — {Program}");
    }
}
