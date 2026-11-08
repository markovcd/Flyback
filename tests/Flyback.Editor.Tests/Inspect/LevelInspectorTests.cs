using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Flyback.Editor.Composition;
using Flyback.Editor.Hubs;
using Flyback.Editor.Inspect;
using Flyback.Engine.Language;
using Shouldly;

namespace Flyback.Editor.Tests.Inspect;

/// <summary>The inspector's panel for a level the text opened, once its modules are the selection.</summary>
public class LevelInspectorTests : EditorTest
{
    private const string Text =
        """
        def voice(freq) = sine(freq: freq) |> drive(drive: 2)
        group "Bass" {
          let bass = voice(55) |> drive(drive: 4)
        }
        let pads = for pitch in [220, 330] { voice(pitch) |> drive(drive: 3) }
        bass + sum(pads) |> out.left
        """;

    private MainWindow Opened(string text = Text)
    {
        var window = NewMainWindow();

        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        Service<Document>(window).Apply(text).ShouldBeNull();
        Settle(window);

        return window;
    }

    private void Choose(MainWindow window, string label)
    {
        var levels = Service<PatchLevels>(window);
        var level = Level.Every(levels.Levels).First(level => level.Label == label);
        var editor = Editor(window);

        editor.Selection.Take(levels.Present(level));
        editor.Selection.Announce();
        Settle(window);
    }

    private static string[] Lines(MainWindow window) =>
        [.. All<TextBlock>(window).Where(t => t.FontSize == Ui.Controls.Text.Body).Select(t => t.Text ?? "")];

    [AvaloniaFact]
    public void A_call_shows_what_it_was_given_where_its_wires_go_and_the_other_calls_of_its_def()
    {
        var window = Opened();

        Choose(window, "voice");

        All<TextBlock>(window).Single(t => t.Name == "level-title").Text.ShouldBe("voice(55)");
        Lines(window).ShouldContain("freq = 55");
        Lines(window).ShouldContain(line => line.StartsWith("Drive.out → bass.", StringComparison.Ordinal));
        All<Button>(window).Where(b => b.Name?.StartsWith("call-", StringComparison.Ordinal) == true).Select(b => (string)b.Content!)
            .ShouldBe(["voice(220)", "voice(330)"]);
    }

    [AvaloniaFact]
    public void A_loop_pass_shows_what_it_took()
    {
        var window = Opened();

        Choose(window, "pass 2/2");

        All<TextBlock>(window).Single(t => t.Name == "level-title").Text.ShouldBe("pass 2/2");
        Lines(window).ShouldContain("pitch = 330");
    }

    [AvaloniaFact]
    public void Show_in_text_opens_the_text_at_the_level()
    {
        var window = Opened();
        var document = Service<Document>(window);

        Choose(window, "voice");
        All<Button>(window).Single(b => b.Name == LevelInspector.ShowInTextName).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Settle(window);

        document.ShowingCode.ShouldBeTrue();
        var text = Controls.SourceViewTest.Text(window);
        document.Text[..text.CaretOffset].Count(c => c == '\n').ShouldBe(2);
    }

    [AvaloniaFact]
    public void One_module_alone_shows_the_module_not_a_level()
    {
        var window = Opened();
        var editor = Editor(window);

        editor.Selection.Select(editor.History.Patch.Nodes.First(n => n.TypeId == "osc.sine").Id);
        Settle(window);

        All<TextBlock>(window).ShouldNotContain(t => t.Name == "level-title");
    }

    [AvaloniaFact]
    public void A_box_a_loop_pass_made_names_its_pass_on_the_group_panel()
    {
        var window = Opened(
            """
            let pads = for pitch in [220, 330] {
              group "Pad" {
                let p = sine(freq: pitch) |> drive(drive: 3)
              }
              p
            }
            sum(pads) |> out.left
            """);
        var editor = Editor(window);

        editor.Selection.SelectGroup(editor.History.Patch.Groups!.Single(group => group.Name == "Pad 2"));
        Settle(window);

        All<WrapPanel>(window).Single(p => p.Name == LevelPath.Name).Children.OfType<Button>().Select(b => (string)b.Content!)
            .ShouldBe(["pads = for pitch ×2", "pass 2/2  330", "Pad 2"]);
    }
}
