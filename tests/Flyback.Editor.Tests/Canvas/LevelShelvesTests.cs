using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Flyback.Editor.Canvas;
using Flyback.Editor.Composition;
using Flyback.Editor.Hubs;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Tests.Canvas;

/// <summary>The shelf chips over the bands an open box holds.</summary>
public class LevelShelvesTests : EditorTest
{
    private const string Text =
        """
        def voice(freq) = sine(freq: freq) |> drive(drive: 2)
        group "Band" {
          let lead = saw(freq: 2) |> drive(drive: 3)
          group "Bass" {
            let bass = voice(55) |> drive(drive: 4)
          }
        }
        lead + bass |> out.left
        """;

    private const string Flat =
        """
        group "Band" {
          let lead = saw(freq: 2) |> drive(drive: 3)
        }
        lead |> out.left
        """;

    private MainWindow Opened(string text = Text, bool shut = false)
    {
        var window = NewMainWindow();

        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        Service<Document>(window).Apply(text).ShouldBeNull();

        var editor = Editor(window);
        foreach (var group in editor.History.Patch.Groups!) group.Collapsed = shut;
        editor.View.FrameAll();

        // Close enough for a module's rows to read, where every chip shows.
        while (editor.View.Zoom < 1) editor.View.ZoomAt(default, 1);
        Settle(window);

        return window;
    }

    private IReadOnlyList<LevelShelves.Shelf> Shelves(MainWindow window)
    {
        var editor = Editor(window);
        return [.. Service<LevelShelves>(window).Of(editor.History.Patch, editor.Selection.Scene)];
    }

    /// <summary>Looks closely at <paramref name="shelf"/>'s chip, so it is on the screen, and clicks it.</summary>
    private static void Press(MainWindow window, LevelShelves.Shelf shelf)
    {
        var editor = Editor(window);

        editor.View.FrameOn(shelf.Chip.Inflate(120));
        Settle(window);

        var at = editor.TranslatePoint(editor.GraphToScreen.Transform(shelf.Chip.Center), window)
            ?? throw new InvalidOperationException("the editor is not in this window");

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);
    }

    [AvaloniaFact]
    public void Each_level_inside_an_open_box_has_a_shelf_named_as_the_text_opened_it() =>
        Shelves(Opened()).Select(shelf => shelf.Name).ShouldBe(["Bass", "voice"]);

    [AvaloniaFact]
    public void A_box_with_no_level_inside_it_has_no_shelves() =>
        Shelves(Opened(Flat)).ShouldBeEmpty();

    [AvaloniaFact]
    public void Pressing_a_chip_selects_every_module_in_its_level()
    {
        var window = Opened();
        var editor = Editor(window);
        var voice = Shelves(window).Single(shelf => shelf.Name == "voice");

        Press(window, voice);

        editor.Selection.Nodes.Select(n => n.TypeId).Order().ShouldBe(["audio.drive", "osc.sine"]);
    }

    [AvaloniaFact]
    public void A_shut_box_lists_the_levels_inside_it_along_its_foot()
    {
        var window = Opened(shut: true);
        var editor = Editor(window);
        var (_, _, box) = editor.Selection.Scene.Boxes().Single();

        var strip = Shelves(window).ShouldHaveSingleItem();

        strip.Name.ShouldBe("Bass");
        strip.OnBox.ShouldBeTrue();
        box.Contains(strip.Chip).ShouldBeTrue();
        strip.Chip.Y.ShouldBeGreaterThan(box.Y + NodeGeometry.HeaderHeight + NodeGeometry.RowHeight * 2);
    }

    [AvaloniaFact]
    public void Pressing_a_chip_on_a_shut_box_selects_the_level_inside_it()
    {
        var window = Opened(shut: true);
        var editor = Editor(window);
        var bass = Shelves(window).Single();

        Press(window, bass);

        editor.Selection.Nodes.Select(n => n.TypeId).Order().ShouldBe(["audio.drive", "audio.drive", "osc.sine"]);
    }

    [AvaloniaFact]
    public void Zoomed_far_out_only_the_outermost_chips_show_at_a_size_that_reads()
    {
        var window = Opened();
        var editor = Editor(window);

        while (editor.View.Zoom >= ModuleOverview.Below) editor.View.ZoomAt(default, -1);
        Settle(window);

        var shelf = Shelves(window).ShouldHaveSingleItem();

        shelf.Name.ShouldBe("Bass");
        (shelf.Chip.Height * editor.View.Zoom).ShouldBe(NodeGeometry.GroupHandleHeight, 0.01);
    }
}
