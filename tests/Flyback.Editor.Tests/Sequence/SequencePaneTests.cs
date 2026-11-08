using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;
using Flyback.Editor.Tests.Controls;
using Flyback.Editor.Sequence;
using Flyback.Engine.Language;
using Shouldly;

namespace Flyback.Editor.Tests.Sequence;

/// <summary>The pane under the canvas that shows the patch's Song.</summary>
public class SequencePaneTests : EditorTest
{
    private static Patch Written(string source)
    {
        var load = PatchLanguage.Build(source, NodeCatalog.BuiltIn);
        load.Ok.ShouldBeTrue(load.Report);
        return load.Patch;
    }

    private static readonly string WithSong = "let song = song() [ kick: 1 0 1 0 bass: 0 1 ]\nsong.kick |> out.left";

    private static DockPanel Pane(Window window) => All<DockPanel>(window).Single(panel => panel.Name == "sequencePane");

    [AvaloniaFact]
    public void It_shows_only_while_the_patch_has_a_Song()
    {
        var window = Open(Written("sine() |> out.left"));
        Pane(window).IsVisible.ShouldBeFalse();

        Editor(window).History.Open(Written(WithSong));
        Settle(window);

        Pane(window).IsVisible.ShouldBeTrue();
        All<SongRows>(window).Single().Parts.Parts.Select(part => part.Name).ShouldBe(["kick", "bass"]);
    }

    [AvaloniaFact]
    public void An_edit_in_the_pane_lands_on_the_Song_and_undoes()
    {
        var window = Open(Written(WithSong));
        var rows = All<SongRows>(window).Single();
        var at = rows.TranslatePoint(new Point(rows.Layout.X(1.5), rows.Layout.RowTop(0) + rows.Layout.Row / 2), window)!.Value;

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);

        Song(window).Levels.Select(level => level.Value).ShouldBe([1f, 1f, 1f, 0f]);

        Editor(window).History.Undo();
        Settle(window);

        Song(window).Levels.Select(level => level.Value).ShouldBe([1f, 0f, 1f, 0f]);
        rows.Parts.Levels(0)[1].Value.ShouldBe(0f);
    }

    [AvaloniaFact]
    public void An_edit_in_the_pane_is_written_into_the_text_that_owns_the_patch()
    {
        var window = Open();
        SourceViewTest.Evaluate(window, "let song = song() [\n    kick: 1 0 1 0\n    bass: 0 1\n]\nsong.kick |> out.left");
        Settle(window);

        var rows = All<SongRows>(window).Single();
        var at = rows.TranslatePoint(new Point(rows.Layout.X(0.5), rows.Layout.RowTop(1) + rows.Layout.Row / 2), window)!.Value;

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);

        SourceViewTest.Text(window).Text.ShouldContain("    bass: 1 1");
        SourceViewTest.Text(window).Text.ShouldContain("song.kick |> out.left");
    }

    private static SongPart Song(Window window) =>
        SongExtra.Of(Editor((Composition.MainWindow)window).History.Patch.FirstOf(SongModule.TypeId)!)[0];
}
