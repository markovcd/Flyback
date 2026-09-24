using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Flyback.Viewer;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// The patch's knobs over a full-window picture, in the editor and in the viewer:
/// there to be played, shown and hidden on request, and absent where the patch has none.
/// </summary>
public class StageKnobTests : UiTest
{
    /// <summary>A Value module coloring the picture, following a knob named Glow unless there is none.</summary>
    private static (Patch Patch, PatchControl? Knob) Board(bool knob = true)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var output = b.Add(NodeCatalog.OutputTypeId, 700, 40);
        var value = b.Add("value", 200, 40, (0, 0.2f));

        b.Wire(value, 0, output, NodeCatalog.OutputColorPort);

        if (!knob) return (b.Patch, null);

        var control = b.Patch.AddControl("Glow", 0.2f);

        ControlMap.Link(value, 0, new ControlLink(control.Id, 0, 1));

        return (b.Patch, control);
    }

    private MainWindow FullScreen(Patch patch)
    {
        var window = NewMainWindow();

        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        All<NodeEditor>(window).Single().History.Open(patch);
        Settle(window);

        var preview = All<PreviewHost>(window).Single();
        var at = preview.TranslatePoint(new Point(preview.Bounds.Width / 2, preview.Bounds.Height / 3), window)!.Value;

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);

        return window;
    }

    private static StageKnobs Stage(Visual window) => All<StageKnobs>(window).Single();

    private static TransportOverlay Transport(Visual window) => All<TransportOverlay>(window).Single();

    /// <summary>Brings the pointer to a stage's dots, which opens it.</summary>
    private static void Reach(Window window, TuckedAway stage)
    {
        var dots = stage.Dots;

        window.MouseMove(dots.TranslatePoint(new Point(dots.Bounds.Width / 2, dots.Bounds.Height / 2), window)!.Value);
        Settle(window);
    }

    private static void TurnUp(Window window, Control knob, double by)
    {
        Reach(window, All<StageKnobs>(window).Single());

        var from = knob.TranslatePoint(new Point(knob.Bounds.Width / 2, knob.Bounds.Height / 2), window)!.Value;

        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(from - new Point(0, by));
        window.MouseUp(from - new Point(0, by), MouseButton.Left);
        Settle(window);
    }

    // --- the editor ----------------------------------------------------------

    [AvaloniaFact]
    public void A_patch_with_knobs_shows_them_over_the_full_screen_picture()
    {
        var (patch, _) = Board();
        var window = FullScreen(patch);

        Stage(window).IsEffectivelyVisible.ShouldBeTrue();
        Stage(window).Knobs.Count.ShouldBe(1);
    }

    [AvaloniaFact]
    public void The_knobs_wait_behind_dots_until_the_pointer_reaches_for_them()
    {
        var (patch, knob) = Board();
        var window = FullScreen(patch);

        Stage(window).IsOpen.ShouldBeFalse("tucked away, as the transport is");

        Reach(window, Stage(window));

        Stage(window).IsOpen.ShouldBeTrue();
        Stage(window).Knobs[knob!.Id].IsEffectivelyVisible.ShouldBeTrue();
        Stage(window).DotsOpacity.ShouldBe(0, "the knobs stand where the dots were");
    }

    [AvaloniaFact]
    public void A_long_row_of_knobs_keeps_clear_of_the_transport()
    {
        var (patch, _) = Board();

        for (var i = 0; i < 40; i++) patch.AddControl($"Knob {i}", 0.5f);

        var window = FullScreen(patch);

        Reach(window, Stage(window));
        Reach(window, Transport(window));

        Transport(window).IsOpen.ShouldBeTrue();

        var transport = Transport(window);
        var corner = new Rect(transport.TranslatePoint(default, window)!.Value, transport.Bounds.Size);

        foreach (var knob in Stage(window).Knobs.Values)
        {
            var at = new Rect(knob.TranslatePoint(default, window)!.Value, knob.Bounds.Size);

            at.Intersects(corner).ShouldBeFalse();
        }
    }

    [AvaloniaFact]
    public void A_patch_with_no_knobs_shows_none()
    {
        var (patch, _) = Board(knob: false);
        var window = FullScreen(patch);

        Stage(window).IsVisible.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void The_knobs_are_not_over_the_picture_outside_full_screen()
    {
        var (patch, _) = Board();
        var window = FullScreen(patch);

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Settle(window);

        Stage(window).IsVisible.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void Turning_a_knob_over_the_picture_turns_the_patch_and_the_panel()
    {
        var (patch, knob) = Board();
        var window = FullScreen(patch);

        TurnUp(window, Stage(window).Knobs[knob!.Id], 80);

        knob.Value.ShouldBeGreaterThan(0.5f);
        All<Knob>(All<ControlsPanel>(window).Single()).Single().Value.ShouldBe(knob.Value, 1e-6);
        All<Grid>(window).First(g => g.Name == "columns").IsEffectivelyVisible.ShouldBeTrue();
        All<NodeEditor>(window).Single().IsEffectivelyVisible.ShouldBeFalse("turning a knob is not asking for the window back");
    }

    [AvaloniaFact]
    public void A_knob_turned_on_the_panel_is_where_the_panel_left_it_over_the_picture()
    {
        var (patch, knob) = Board();
        var window = FullScreen(patch);

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Settle(window);

        TurnUp(window, All<Knob>(All<ControlsPanel>(window).Single()).Single(), 60);

        var preview = All<PreviewHost>(window).Single();
        var at = preview.TranslatePoint(new Point(preview.Bounds.Width / 2, preview.Bounds.Height / 3), window)!.Value;

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);

        knob!.Value.ShouldBeGreaterThan(0.3f);
        Stage(window).Knobs[knob.Id].Value.ShouldBe(knob.Value, 1e-6);
    }

    [AvaloniaFact]
    public void A_knob_says_nothing_but_its_tip_with_its_name_and_reading()
    {
        var (patch, knob) = Board();
        var window = FullScreen(patch);

        All<TextBlock>(Stage(window)).ShouldBeEmpty("a played knob carries no text");
        ToolTip.GetTip(Stage(window).Knobs[knob!.Id]).ShouldBe("Glow  0.2");
    }

    // --- full screen on another monitor --------------------------------------

    private MainWindow SentAway(Patch patch)
    {
        var window = NewMainWindow();

        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        All<NodeEditor>(window).Single().History.Open(patch);
        Settle(window);

        window.ShowPictureOn(window.Screens.ScreenFromWindow(window)!);
        Settle(window);

        return window;
    }

    [AvaloniaFact]
    public void The_knobs_go_with_the_picture_to_another_monitor_and_come_back()
    {
        var (patch, knob) = Board();
        var window = SentAway(patch);
        var picture = window.OwnedWindows.Single();

        TopLevel.GetTopLevel(Stage(picture)).ShouldBeSameAs(picture);
        Stage(picture).IsEffectivelyVisible.ShouldBeTrue();

        TurnUp(picture, Stage(picture).Knobs[knob!.Id], 80);

        knob.Value.ShouldBeGreaterThan(0.5f);
        All<Knob>(All<ControlsPanel>(window).Single()).Single().Value.ShouldBe(knob.Value, 1e-6, "the editor's panel follows the picture's knob");

        picture.Close();
        Settle(window);

        Stage(window).IsVisible.ShouldBeFalse("back in the editor, and out of the way");
    }

    // --- the viewer ----------------------------------------------------------

    private ViewerWindow Viewer(Patch patch, bool noOverlay = false)
    {
        var options = new ViewerOptions { Gpu = false, Size = new PixelSize(320, 180), NoOverlay = noOverlay };
        var window = Owned(ViewerServices.Window(new ViewerLaunch(new Opened(patch, new SampleLibrary(), new ImageLibrary()), null, options)));

        window.Show();
        Settle(window);

        return window;
    }

    [AvaloniaFact]
    public void The_viewer_shows_a_patch_s_knobs_and_turning_one_plays_it()
    {
        var (patch, knob) = Board();
        var window = Viewer(patch);

        Stage(window).IsEffectivelyVisible.ShouldBeTrue();

        TurnUp(window, Stage(window).Knobs[knob!.Id], 80);

        window.Player.Patch.Control(knob.Id)!.Value.ShouldBeGreaterThan(0.5f);
    }

    [AvaloniaFact]
    public void The_viewer_shows_no_knobs_for_a_patch_with_none()
    {
        var (patch, _) = Board(knob: false);
        var window = Viewer(patch);

        Stage(window).IsVisible.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void The_viewer_asked_for_no_overlay_has_no_knobs_either()
    {
        var (patch, _) = Board();
        var window = Viewer(patch, noOverlay: true);

        All<StageKnobs>(window).ShouldBeEmpty();
    }
}
