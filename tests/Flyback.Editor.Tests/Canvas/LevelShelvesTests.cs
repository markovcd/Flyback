using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Flyback.Editor.Canvas;
using Flyback.Editor.Composition;
using Flyback.Editor.Hubs;
using Flyback.Editor.Settings;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Tests.Canvas;

/// <summary>The shelf chips over the bands an open box holds, while the levels are laid out as bands.</summary>
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

    private MainWindow Opened(bool bands)
    {
        var window = NewMainWindow();

        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        Service<OutputSettingRepository>(window).Current.LevelBands = bands;
        Service<Document>(window).Apply(Text).ShouldBeNull();

        var editor = Editor(window);
        foreach (var group in editor.History.Patch.Groups!) group.Collapsed = false;
        editor.View.FrameAll();
        Settle(window);

        return window;
    }

    private IReadOnlyList<LevelShelves.Shelf> Shelves(MainWindow window)
    {
        var editor = Editor(window);
        return [.. Service<LevelShelves>(window).Of(editor.History.Patch, editor.Selection.Scene)];
    }

    [AvaloniaFact]
    public void Each_level_inside_an_open_box_has_a_shelf_named_as_the_text_opened_it() =>
        Shelves(Opened(bands: true)).Select(shelf => shelf.Band.Label).ShouldBe(["Bass", "voice"]);

    [AvaloniaFact]
    public void Without_bands_there_are_no_shelves() =>
        Shelves(Opened(bands: false)).ShouldBeEmpty();

    [AvaloniaFact]
    public void Pressing_a_chip_selects_every_module_in_its_level()
    {
        var window = Opened(bands: true);
        var editor = Editor(window);
        var voice = Shelves(window).Single(shelf => shelf.Band.Label == "voice");

        var at = editor.TranslatePoint(editor.GraphToScreen.Transform(voice.Chip.Center), window)
            ?? throw new InvalidOperationException("the editor is not in this window");

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);

        editor.Selection.Nodes.Select(n => n.TypeId).Order().ShouldBe(["audio.drive", "osc.sine"]);
    }
}
