using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Flyback.Core.Graph;
using Flyback.Editor.Canvas;
using Shouldly;

namespace Flyback.Editor.Tests.Canvas;

/// <summary>
/// Zoomed out past reading, as a patch framed on a phone is, modules show their names
/// alone; a double tap on one brings it up to a size that reads, and one on empty canvas
/// looks at the whole patch again.
/// </summary>
public class ModuleOverviewTests : EditorTest
{
    private static Pointer Finger() => new(Pointer.GetNextFreeId(), PointerType.Touch, false);

    private static void Tap(NodeEditor editor, Point at, ulong time)
    {
        var finger = Finger();

        editor.Fingers.Down(editor, finger, at, time);
        editor.Fingers.Up(editor, finger, at, time + 50);
    }

    private static void DoubleTap(NodeEditor editor, Point at)
    {
        Tap(editor, at, 1_000);
        Tap(editor, at, 1_200);
    }

    private static Patch Row(out NodeInstance sine)
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);

        builder.Add("time", 0, 0);
        sine = builder.Add(NodeCatalog.SineTypeId, 600, 0);
        builder.Add(NodeCatalog.OutputTypeId, 3000, 1200);

        return builder.Patch;
    }

    private static Point Middle(NodeEditor editor, NodeInstance node) =>
        editor.View.OnScreen(editor.Selection.Scene.Footprint(node)).Center;

    [AvaloniaFact]
    public void A_patch_framed_on_a_phone_is_drawn_as_names()
    {
        var (editor, window) = Editing(Row(out _), 412, 700);
        Settle(window);

        editor.View.Overview.ShouldBeTrue();
        Shoot(window, "overview.png");
    }

    [AvaloniaFact]
    public void A_double_tap_on_a_module_zoomed_out_brings_it_up_to_a_size_that_reads()
    {
        var (editor, window) = Editing(Row(out var sine), 412, 700);
        Settle(window);

        DoubleTap(editor, Middle(editor, sine));
        Settle(window);

        editor.View.Overview.ShouldBeFalse();
        editor.View.Zoom.ShouldBe(1);
        Middle(editor, sine).X.ShouldBe(412 / 2d, 1);
        editor.History.CanUndo.ShouldBeFalse();
        Shoot(window, "overview-zoomed-to-module.png");
    }

    [AvaloniaFact]
    public void A_module_taller_than_the_view_shows_its_top()
    {
        var (editor, window) = Editing(Row(out var sine), 412, 80);
        Settle(window);

        DoubleTap(editor, Middle(editor, sine));

        editor.View.Zoom.ShouldBe(ModuleOverview.Below);
        editor.View.OnScreen(editor.Selection.Scene.Footprint(sine)).Top.ShouldBe(16, 1);
    }

    [AvaloniaFact]
    public void A_double_tap_on_empty_canvas_looks_at_the_whole_patch_again()
    {
        var (editor, window) = Editing(Row(out var sine), 412, 700);
        Settle(window);
        var framed = (editor.View.Zoom, editor.View.Pan);

        DoubleTap(editor, Middle(editor, sine));
        DoubleTap(editor, new Point(30, 650));

        (editor.View.Zoom, editor.View.Pan).ShouldBe(framed);
    }

    [AvaloniaFact]
    public void A_double_tap_on_a_module_that_reads_already_leaves_the_view_alone()
    {
        var (editor, window) = Editing(Row(out var sine), 412, 700);
        Settle(window);
        editor.View.FrameOn(editor.Selection.Scene.Footprint(sine));
        var was = (editor.View.Zoom, editor.View.Pan);

        DoubleTap(editor, Middle(editor, sine));

        (editor.View.Zoom, editor.View.Pan).ShouldBe(was);
    }

    [AvaloniaFact]
    public void A_tap_where_a_hidden_well_would_be_selects_the_module_and_picks_nothing()
    {
        var (editor, window) = Editing(Row(out var sine), 412, 700);
        Settle(window);
        var def = NodeCatalog.Get(sine.TypeId)!;
        var freq = def.Inputs.ToList().FindIndex(port => port.Name == "freq");
        var well = editor.GraphToScreen.Transform(ValueWell.At(Geometry.Bounds(sine, def), Geometry.InputPort(sine, def, freq)).Center);

        Tap(editor, well, 1_000);

        editor.WellFocus.Focused.ShouldBeNull();
        editor.StepBar.Box.ShouldBeNull();
        editor.Selection.Ids.ShouldBe([sine.Id]);
    }
}
