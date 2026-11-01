using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Flyback.Core.Graph;
using Flyback.Editor.Canvas;
using Flyback.Editor.Inspect;
using Flyback.Editor.Shell;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Tests.Canvas;

/// <summary>
/// On a Mac a scroll pans the canvas and a pinch zooms it, as two fingers on a trackpad
/// do in every other Mac program; Ctrl or Cmd with a scroll still zooms, for a mouse.
/// Elsewhere the wheel zooms.
/// </summary>
public class TouchpadScrollTests : EditorTest
{
    private const double Wide = 1200;
    private const double Tall = 800;

    private static readonly Point Middle = new(Wide / 2, Tall / 2);

    private (NodeEditor Editor, Window Window) Scrolling(bool scrollPans)
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        builder.Add("value", 0, 0);
        builder.Add(NodeCatalog.OutputTypeId, 520, 0);

        var editor = NewCanvas(Wide, Tall);
        var window = Show(editor, Wide);

        editor.History.Open(builder.Patch);
        Settle(window);

        editor.Gestures.ScrollPans = scrollPans;
        return (editor, window);
    }

    private static void Pinch(NodeEditor editor, Window window, Point at, double magnification)
    {
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
        var properties = new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other);

        editor.RaiseEvent(new PointerDeltaEventArgs(
            InputElement.PointerTouchPadGestureMagnifyEvent, editor, pointer, window, at, 1_000, properties, KeyModifiers.None, new Vector(magnification, magnification)));
        Settle(window);
    }

    [AvaloniaFact]
    public void Two_fingers_scrolling_pan_the_view_and_leave_the_zoom()
    {
        var (editor, window) = Scrolling(scrollPans: true);
        var zoom = editor.View.Zoom;
        var pan = editor.View.Pan;

        window.MouseWheel(Middle, new Vector(0.4, -0.6));
        Settle(window);

        editor.View.Zoom.ShouldBe(zoom);
        editor.View.Pan.X.ShouldBe(pan.X + 0.4 * CanvasGestures.PanPerNotch, 0.001);
        editor.View.Pan.Y.ShouldBe(pan.Y - 0.6 * CanvasGestures.PanPerNotch, 0.001);
    }

    [AvaloniaTheory]
    [InlineData(RawInputModifiers.Control)]
    [InlineData(RawInputModifiers.Meta)]
    public void Ctrl_or_Cmd_with_a_scroll_zooms_about_the_pointer(RawInputModifiers held)
    {
        var (editor, window) = Scrolling(scrollPans: true);
        var zoom = editor.View.Zoom;
        var under = editor.View.ToGraph(Middle);

        window.MouseWheel(Middle, new Vector(0, 1), held);
        Settle(window);

        editor.View.Zoom.ShouldBeGreaterThan(zoom);
        editor.View.ToGraph(Middle).X.ShouldBe(under.X, 0.001);
        editor.View.ToGraph(Middle).Y.ShouldBe(under.Y, 0.001);
    }

    [AvaloniaFact]
    public void Without_scroll_panning_the_wheel_zooms()
    {
        var (editor, window) = Scrolling(scrollPans: false);
        var zoom = editor.View.Zoom;

        window.MouseWheel(Middle, new Vector(0, 1));
        Settle(window);

        editor.View.Zoom.ShouldBeGreaterThan(zoom);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Spreading_two_fingers_zooms_in_about_the_pointer_and_pinching_zooms_out(bool scrollPans)
    {
        var (editor, window) = Scrolling(scrollPans);
        var zoom = editor.View.Zoom;
        var at = new Point(300, 200);
        var under = editor.View.ToGraph(at);

        Pinch(editor, window, at, 0.25);

        editor.View.Zoom.ShouldBe(zoom * 1.25, 0.0001);
        editor.View.ToGraph(at).X.ShouldBe(under.X, 0.001);
        editor.View.ToGraph(at).Y.ShouldBe(under.Y, 0.001);

        Pinch(editor, window, at, -0.2);

        editor.View.Zoom.ShouldBe(zoom * 1.25 * 0.8, 0.0001);
    }

    [AvaloniaFact]
    public void The_host_says_whether_a_scroll_pans()
    {
        var mac = Editor(Open(setup: new EditorSetup { Host = new EditorHost { ScrollPans = true } }));
        var other = Editor(Open());

        mac.Gestures.ScrollPans.ShouldBeTrue();
        other.Gestures.ScrollPans.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void The_help_names_the_scroll_and_the_pinch_where_a_scroll_pans()
    {
        var rows = InspectorHelp.Shortcuts(inPage: false, fingers: false, scrollPans: true).SelectMany(g => g.Rows).ToList();

        rows.Single(r => r.Title == "Pan").Keys.ShouldBe("Scroll");
        rows.Single(r => r.Title == "Zoom").Detail.ShouldContain("Pinch");
        InspectorHelp.Locked(fingers: false, scrollPans: true).ShouldContain("pinch to zoom");
    }
}
