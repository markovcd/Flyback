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
/// On a Mac two fingers on a trackpad pan the canvas and a pinch zooms it, while a mouse
/// wheel zooms; Ctrl or Cmd with a scroll zooms. Elsewhere the wheel zooms, and the
/// Canvas settings can make every scroll pan or zoom.
/// </summary>
public class TouchpadScrollTests : EditorTest
{
    private const double Wide = 1200;
    private const double Tall = 800;

    private static readonly Point Middle = new(Wide / 2, Tall / 2);

    private (NodeEditor Editor, Window Window) Scrolling(bool scrollPans, bool? mouse = null, CanvasScroll scroll = CanvasScroll.Auto)
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        builder.Add("value", 0, 0);
        builder.Add(NodeCatalog.OutputTypeId, 520, 0);

        var editor = NewCanvas(Wide, Tall);
        var window = Show(editor, Wide);

        editor.History.Open(builder.Patch);
        Settle(window);

        editor.Gestures.ScrollPans = scrollPans;
        editor.Gestures.ScrollIsMouse = () => mouse;
        editor.Gestures.Scroll = scroll;
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

    [AvaloniaFact]
    public void On_a_Mac_a_mouse_wheel_zooms_about_the_pointer()
    {
        var (editor, window) = Scrolling(scrollPans: true, mouse: true);
        var zoom = editor.View.Zoom;
        var under = editor.View.ToGraph(Middle);

        window.MouseWheel(Middle, new Vector(0, 1));
        Settle(window);

        editor.View.Zoom.ShouldBeGreaterThan(zoom);
        editor.View.ToGraph(Middle).X.ShouldBe(under.X, 0.001);
        editor.View.ToGraph(Middle).Y.ShouldBe(under.Y, 0.001);
    }

    [AvaloniaFact]
    public void A_scroll_keeps_what_its_first_notch_did_until_it_rests()
    {
        var mouse = false;
        var (editor, window) = Scrolling(scrollPans: true);
        editor.Gestures.ScrollIsMouse = () => mouse;
        var zoom = editor.View.Zoom;

        window.MouseWheel(Middle, new Vector(0, -0.5));
        mouse = true;
        window.MouseWheel(Middle, new Vector(0, -0.5));
        Settle(window);

        editor.View.Zoom.ShouldBe(zoom, "a trackpad stroke pans to its end, whatever its momentum looks like");
    }

    [AvaloniaTheory]
    [InlineData(true, null)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void Set_to_pan_every_scroll_pans(bool scrollPans, bool? mouse)
    {
        var (editor, window) = Scrolling(scrollPans, mouse, CanvasScroll.Pan);
        var zoom = editor.View.Zoom;
        var pan = editor.View.Pan;

        window.MouseWheel(Middle, new Vector(0, -1));
        Settle(window);

        editor.View.Zoom.ShouldBe(zoom);
        editor.View.Pan.Y.ShouldBe(pan.Y - CanvasGestures.PanPerNotch, 0.001);
    }

    [AvaloniaTheory]
    [InlineData(true, null)]
    [InlineData(true, false)]
    public void Set_to_zoom_every_scroll_zooms(bool scrollPans, bool? mouse)
    {
        var (editor, window) = Scrolling(scrollPans, mouse, CanvasScroll.Zoom);
        var zoom = editor.View.Zoom;

        window.MouseWheel(Middle, new Vector(0, 1));
        Settle(window);

        editor.View.Zoom.ShouldBeGreaterThan(zoom);
    }

    [AvaloniaFact]
    public void A_saved_scroll_reads_back_and_a_damaged_one_is_automatic()
    {
        CanvasSettings.Parse(new CanvasSettings { Scroll = CanvasScroll.Zoom }.Json()).Scroll.ShouldBe(CanvasScroll.Zoom);
        CanvasSettings.Parse("""{ "scroll": "Sideways" }""").Scroll.ShouldBe(CanvasScroll.Auto);
        CanvasSettings.Parse("""{ "scroll": 7 }""").Scroll.ShouldBe(CanvasScroll.Auto);
        CanvasSettings.Parse("{}").Scroll.ShouldBe(CanvasScroll.Auto);
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
        var rows = InspectorHelp.Shortcuts(new EditorHost(), fingers: false, scroll: CanvasScroll.Pan).SelectMany(g => g.Rows).ToList();

        rows.Single(r => r.Title == "Pan").Keys.ShouldBe("Scroll");
        rows.Single(r => r.Title == "Zoom").Detail.ShouldContain("Pinch");
        InspectorHelp.Locked(fingers: false, scroll: CanvasScroll.Pan).ShouldContain("pinch to zoom");
    }

    [AvaloniaFact]
    public void The_help_on_a_Mac_names_the_wheel_for_zoom_and_the_trackpad_for_pan()
    {
        var rows = InspectorHelp.Shortcuts(new EditorHost(), fingers: false, scroll: CanvasScroll.Auto).SelectMany(g => g.Rows).ToList();

        rows.Single(r => r.Title == "Pan").Detail.ShouldContain("trackpad");
        rows.Single(r => r.Title == "Zoom").Keys.ShouldBe("Wheel");
        rows.Single(r => r.Title == "Zoom").Detail.ShouldContain("pinch");
        InspectorHelp.Locked(fingers: false, scroll: CanvasScroll.Auto).ShouldContain("wheel or pinch to zoom");
    }

    [AvaloniaFact]
    public void The_scroll_mode_is_automatic_only_where_a_scroll_pans()
    {
        var (editor, _) = Scrolling(scrollPans: true);
        editor.Gestures.ScrollMode.ShouldBe(CanvasScroll.Auto);

        editor.Gestures.ScrollPans = false;
        editor.Gestures.ScrollMode.ShouldBe(CanvasScroll.Zoom);

        editor.Gestures.Scroll = CanvasScroll.Pan;
        editor.Gestures.ScrollMode.ShouldBe(CanvasScroll.Pan);
    }
}
