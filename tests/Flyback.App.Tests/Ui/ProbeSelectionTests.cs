using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Flyback.App.Controls;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// Selecting a Probe is what puts its chart on the screen, and selecting
/// anything else is what puts the picture back.
/// </summary>
/// <remarks>
/// The rule lives in the shell rather than in the patch on purpose: a chart is
/// something you look at, not a state an instrument is left in. Nothing about
/// the file changes while one is up, and nothing about the sound does either —
/// the speakers root at the Output whatever the screen has been asked for.
/// </remarks>
public class ProbeSelectionTests : UiTest
{
    private static MainWindow Open(Patch patch)
    {
        var window = new MainWindow();

        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        Editor(window).Patch = patch;

        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        return window;
    }

    private static NodeEditor Editor(MainWindow window) => All<NodeEditor>(window).Single();

    private static CompiledPatch Showing(MainWindow window) => All<PreviewHost>(window).Single().Program;

    private static void Select(MainWindow window, NodeInstance node)
    {
        var editor = Editor(window);

        var body = new Point(
            node.X + NodeGeometry.Width / 2,
            node.Y + NodeGeometry.HeaderHeight / 2);

        var at = editor.TranslatePoint(editor.GraphToScreen.Transform(body), window)
            ?? throw new InvalidOperationException("the editor is not in this window");

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);

        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// A picture with one oscillator in it, and a probe on a second one that
    /// reaches nothing else. Which program is on the screen can then be read off
    /// the ops: the picture's oscillator is the only Sin in the patch, and the
    /// chart never walks as far as the Output to find it.
    /// </summary>
    private static (Patch Patch, NodeInstance Probe, NodeInstance Elsewhere) Patched()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var output = b.Add(NodeCatalog.OutputTypeId, 700, 40);
        var coordinates = b.Add("coord", 40, 40);
        var picture = b.Add("osc.sine", 340, 40);

        b.Wire(coordinates, 0, picture, 0).Wire(picture, 0, output, NodeCatalog.OutputColorPort);

        var clock = b.Add("time", 40, 320);
        var probe = b.Add(NodeCatalog.ProbeTypeId, 340, 320);

        b.Wire(clock, 0, probe, 0);

        return (b.Patch, probe, coordinates);
    }

    private static int Sines(CompiledPatch program) => program.Ops.Count(op => op.Code == OpCode.Sin);

    [AvaloniaFact]
    public void A_probe_shows_its_chart_only_while_it_is_the_selected_module()
    {
        var (patch, probe, elsewhere) = Patched();
        var window = Open(patch);

        Sines(Showing(window)).ShouldBe(1, "the patch, with nothing selected");

        Select(window, probe);
        Sines(Showing(window)).ShouldBe(0, "the chart, which never walks to the Output");

        Select(window, elsewhere);
        Sines(Showing(window)).ShouldBe(1, "the patch again");
    }

    /// <summary>
    /// A probe left selected looks exactly like a patch that has stopped
    /// working, and the status line is the only thing in the window that can say
    /// otherwise.
    /// </summary>
    [AvaloniaFact]
    public void The_window_says_that_what_is_on_the_screen_is_a_chart()
    {
        var (patch, probe, elsewhere) = Patched();
        var window = Open(patch);

        Select(window, probe);
        Announced(window).ShouldBeTrue();

        Select(window, elsewhere);
        Announced(window).ShouldBeFalse();
    }

    private static bool Announced(MainWindow window) =>
        All<TextBlock>(window).Any(t => t.Text?.Contains("Showing the Probe") == true);
}
