using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Flyback.Core.Graph;
using Flyback.Editor.Assist;
using Flyback.Editor.Bars;
using Flyback.Editor.Composition;
using Flyback.Editor.Notices;
using Flyback.Editor.Shell;
using Flyback.Editor.Tutorials;
using Flyback.Engine.Graph;
using Flyback.Engine.Language;
using Flyback.Engine.Tutorials;
using Flyback.Plugins.Hosting;
using Shouldly;

namespace Flyback.Editor.Tests.Tutorials;

/// <summary>The tutorials' column: opening on the start, ticking steps off as the patch is edited, and its share of the left column.</summary>
public sealed class TutorialPanelTests : EditorTest
{
    private MainWindow Window() => Open(Presets.Plasma(NodeCatalog.BuiltIn), new EditorSetup { Plugins = PluginCatalog.Empty });

    private TutorialPanel Column(MainWindow window)
    {
        Service<Reactions>(window).Raise(new TutorialAsked(true));
        Settle(window);
        return All<TutorialPanel>(window).Single();
    }

    private static Tutorial FirstPatch => ShippedTutorials.Find("first-patch")!;

    /// <summary>Edits the canvas into what <paramref name="source"/> builds, as one step of history.</summary>
    private static void Edit(MainWindow window, string source)
    {
        var load = PatchLanguage.Build(source, NodeCatalog.BuiltIn);
        load.Issues.ShouldBeEmpty(load.Report);
        Editor(window).History.Apply(load.Patch);
        Settle(window);
    }

    private static void Start(MainWindow window, TutorialPanel panel, Tutorial tutorial)
    {
        panel.StartAsync(tutorial).GetAwaiter().GetResult().ShouldBeTrue();
        Settle(window);
    }

    [AvaloniaFact]
    public void The_column_lists_every_shipped_tutorial()
    {
        var window = Window();
        var panel = Column(window);

        panel.IsVisible.ShouldBeTrue();
        foreach (var tutorial in ShippedTutorials.All)
            All<Button>(panel).ShouldContain(b => b.Name == $"tutorial-{tutorial.Id}");
    }

    [AvaloniaFact]
    public void Starting_a_tutorial_opens_its_preset_and_asks_for_the_first_step()
    {
        var window = Window();
        var panel = Column(window);

        Start(window, panel, FirstPatch);

        Editor(window).History.Patch.Nodes.Select(n => n.TypeId).ShouldBe([NodeCatalog.OutputTypeId]);
        panel.Step.ShouldBe(0);
        panel.Hint.ShouldBe("Add a Sine.");
    }

    [AvaloniaFact]
    public void An_edit_that_does_what_a_step_asks_ticks_it_off_and_moves_on()
    {
        var window = Window();
        var panel = Column(window);
        Start(window, panel, FirstPatch);

        Edit(window, "let wave = sine()");

        panel.ShouldSatisfyAllConditions(
            () => panel.Done[0].ShouldBeTrue(),
            () => panel.Step.ShouldBe(1),
            () => panel.Hint.ShouldBe("Wire the Sine's out into the Output's color."));
    }

    [AvaloniaFact]
    public void An_edit_that_does_several_steps_at_once_ticks_them_all()
    {
        var window = Window();
        var panel = Column(window);
        Start(window, panel, FirstPatch);

        Edit(window, "x |> sine(freq: 4) |> out.color");

        panel.Step.ShouldBe(4);
        panel.Done.Take(4).ShouldAllBe(done => done);
    }

    [AvaloniaFact]
    public void Going_back_stays_on_the_step_until_the_patch_changes()
    {
        var window = Window();
        var panel = Column(window);
        Start(window, panel, FirstPatch);
        Edit(window, "let wave = sine()");

        panel.Back();

        panel.Step.ShouldBe(0);
        panel.Done[0].ShouldBeFalse();
    }

    [AvaloniaFact]
    public void A_step_with_nothing_to_check_is_moved_past_by_hand()
    {
        var window = Window();
        var panel = Column(window);
        Start(window, panel, FirstPatch);
        Edit(window, "rings(offset: t) |> out.color");

        // Steps 2 to 5 are skipped past, then the rings meet the sixth.
        for (var i = 0; i < 4; i++) panel.Next();

        panel.Step.ShouldBe(6);
        panel.Next();
        panel.Step.ShouldBe(FirstPatch.Steps.Count);
        panel.Done[^1].ShouldBeTrue();
    }

    [AvaloniaFact]
    public void The_tutorials_and_the_assistant_take_turns_in_the_column()
    {
        var window = Window();
        var panel = Column(window);
        var bar = Service<StatusBar>(window);

        Service<Reactions>(window).Raise(new AssistantAsked(true));
        Settle(window);

        All<AssistantPanel>(window).Single().IsVisible.ShouldBeTrue();
        panel.IsVisible.ShouldBeFalse();
        bar.Tutorial.IsChecked.ShouldBe(false);
    }
}
