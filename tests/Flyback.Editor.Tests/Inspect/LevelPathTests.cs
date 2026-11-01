using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Flyback.Editor.Composition;
using Flyback.Editor.Hubs;
using Flyback.Editor.Inspect;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Tests.Inspect;

/// <summary>The levels a module the text placed sits in, at the top of the inspector, each selecting its modules.</summary>
public class LevelPathTests : EditorTest
{
    private const string Text =
        """
        def voice(freq) = {
          def shaped(in) = in |> drive(drive: 2)
          sine(freq: freq) |> shaped()
        }
        group "Bass" {
          let bass = voice(55) |> drive(drive: 4)
        }
        bass + saw(freq: 3) |> out.left
        """;

    private MainWindow Opened()
    {
        var window = NewMainWindow();

        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        Service<Document>(window).Apply(Text).ShouldBeNull();
        Settle(window);

        return window;
    }

    private static void Select(MainWindow window, string type, Func<Core.Graph.NodeInstance, bool>? which = null)
    {
        var editor = Editor(window);
        editor.Selection.Select(editor.History.Patch.Nodes.First(n => n.TypeId == type && (which?.Invoke(n) ?? true)).Id);
        Settle(window);
    }

    private static string[] Path(MainWindow window) =>
        [.. All<WrapPanel>(window).Single(p => p.Name == LevelPath.Name).Children.OfType<Button>().Select(b => (string)b.Content!)];

    [AvaloniaFact]
    public void A_module_a_call_inside_a_call_placed_shows_every_level_it_sits_in()
    {
        var window = Opened();

        Select(window, "osc.sine");

        Path(window).ShouldBe(["Bass", "voice"]);
    }

    [AvaloniaFact]
    public void A_module_no_level_placed_shows_no_path()
    {
        var window = Opened();

        Select(window, "osc.saw");

        All<WrapPanel>(window).ShouldNotContain(p => p.Name == LevelPath.Name);
    }

    [AvaloniaFact]
    public void Pressing_a_level_selects_every_module_in_it()
    {
        var window = Opened();
        var editor = Editor(window);

        Select(window, "osc.sine");

        var call = All<Button>(window).Single(b => b.Name == "level-1");
        call.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Settle(window);

        editor.Selection.Nodes.Select(n => n.TypeId).Order().ShouldBe(["audio.drive", "osc.sine"]);
        editor.Selection.Focused!.TypeId.ShouldBe("osc.sine");
        All<Button>(window).Single(b => b.Name == "level-1").FontWeight.ShouldBe(Avalonia.Media.FontWeight.SemiBold);
    }
}
