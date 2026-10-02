using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Flyback.Editor.Knobs;
using Flyback.Editor.Settings;
using Flyback.Editor.Windows;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Shouldly;
using Flyback.Ui;

namespace Flyback.Editor.Desktop.Tests.Ui;

/// <summary>
/// Randomizing the knob panel: the die, Ctrl+Shift+K, a knob held out of it, and
/// the way back.
/// </summary>
public class KnobRandomizeTests : UiTest
{
    private static ControlsPanel Panel(MainWindow window) => All<ControlsPanel>(window).Single();

    /// <summary>A Value module following the first of <paramref name="knobs"/> knobs, each resting at a quarter.</summary>
    private static Patch Board(int knobs)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var output = b.Add(NodeCatalog.OutputTypeId, 700, 40);
        var value = b.Add("value", 200, 40, (0, 0.25f));

        b.Wire(value, 0, output, NodeCatalog.OutputColorPort);

        for (var i = 0; i < knobs; i++) b.Patch.AddControl($"Knob {i + 1}", 0.25f);

        ControlMap.Link(value, 0, new ControlLink(b.Patch.Controls![0].Id, 0f, 1f));

        return b.Patch;
    }

    private MainWindow Opened(Patch patch, int seed = 3)
    {
        var window = Open(patch);

        Service<OutputSettingRepository>(window).Current.Randomize = new RandomizeSettings { Amount = 1, GlideSeconds = 0 };
        Service<KnobRandomizer>(window).Random = new Random(seed);

        return window;
    }

    private static void Press(MainWindow window, string name)
    {
        var button = All<Button>(Panel(window)).Single(b => b.Name == name);

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Settle(window);
    }

    private static List<float> Values(MainWindow window) => [.. Editor(window).History.Patch.Controls!.Select(c => c.Value)];

    [AvaloniaFact]
    public void The_die_moves_every_knob_and_the_sockets_that_follow_them_without_an_edit()
    {
        var window = Opened(Board(knobs: 3));
        var modified = Editor(window).History.IsModified;

        Press(window, "roll-knobs");

        Values(window).ShouldAllBe(value => value != 0.25f);
        Editor(window).History.IsModified.ShouldBe(modified);
    }

    [AvaloniaFact]
    public void A_held_knob_stays_where_it_is()
    {
        var patch = Board(knobs: 2);
        patch.Controls![1].Held = true;
        var window = Opened(patch);

        Press(window, "roll-knobs");

        Values(window)[0].ShouldNotBe(0.25f);
        Values(window)[1].ShouldBe(0.25f);
    }

    [AvaloniaFact]
    public void The_knob_menu_holds_a_knob_and_undo_lets_it_go()
    {
        var window = Opened(Board(knobs: 1));

        var more = All<Button>(Panel(window)).First(b => b.Name == "knob-menu");
        more.Flyout!.ShowAt(more);
        Settle(window);

        var hold = ((MenuFlyout)more.Flyout).Items.OfType<MenuItem>().Single(item => (item.Header as string) == "Hold when randomizing");
        hold.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Settle(window);

        Editor(window).History.Patch.Controls!.Single().Held.ShouldBeTrue();

        Editor(window).History.Undo().ShouldBeTrue();
        Settle(window);

        Editor(window).History.Patch.Controls!.Single().Held.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Ctrl_Shift_K_randomizes()
    {
        var window = Opened(Board(knobs: 2));

        window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Control | RawInputModifiers.Shift);
        Settle(window);

        Values(window).ShouldAllBe(value => value != 0.25f);
        Panel(window).IsVisible.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void Back_returns_the_knobs_to_where_each_randomize_found_them()
    {
        var window = Opened(Board(knobs: 2));

        Press(window, "roll-knobs");
        var first = Values(window);
        Press(window, "roll-knobs");

        Press(window, "unroll-knobs");
        Values(window).ShouldBe(first);

        Press(window, "unroll-knobs");
        Values(window).ShouldAllBe(value => value == 0.25f);

        All<Button>(Panel(window)).Single(b => b.Name == "unroll-knobs").IsEnabled.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void A_glide_takes_its_time_and_arrives()
    {
        var window = Opened(Board(knobs: 1));
        Service<OutputSettingRepository>(window).Current.Randomize.GlideSeconds = 2;
        var randomizer = Service<KnobRandomizer>(window);
        var now = TimeSpan.Zero;
        Action? next = null;
        randomizer.Now = () => now;
        randomizer.After = (_, act) => next = act;

        Press(window, "roll-knobs");
        var halfway = Values(window).Single();

        now = TimeSpan.FromSeconds(1);
        next!();
        var midway = Values(window).Single();

        now = TimeSpan.FromSeconds(2);
        next();
        var landed = Values(window).Single();

        halfway.ShouldBe(0.25f);
        midway.ShouldBe((0.25f + landed) / 2, 1e-5f);
        landed.ShouldNotBe(0.25f);
        randomizer.Gliding.ShouldBeFalse();
    }
}
