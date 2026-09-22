using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Shouldly;
using Colors = Flyback.App.Controls.Colors;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// An Auto remap's panel says what each fraction comes to at the far end of its
/// wire, and outlines the pair left as plain numbers where that end has no range.
/// </summary>
public class AutoRemapInspectorTests : UiTest
{
    private MainWindow Open(Patch patch)
    {
        var window = NewMainWindow();

        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        Editor(window).Patch = patch;
        Settle(window);

        return window;
    }

    private static NodeEditor Editor(MainWindow window) => All<NodeEditor>(window).Single();

    private static Point OnWindow(MainWindow window, Point graph)
    {
        var editor = Editor(window);

        return editor.TranslatePoint(editor.GraphToScreen.Transform(graph), window)
            ?? throw new InvalidOperationException("the editor is not in this window");
    }

    private static void Select(MainWindow window, NodeInstance node)
    {
        var at = OnWindow(window, new Point(node.X + NodeGeometry.Width / 2, node.Y + NodeGeometry.HeaderHeight / 2));

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Settle(window);
    }

    /// <summary>The panel's number boxes, in socket order, with whether each is outlined.</summary>
    private static bool[] Outlined(MainWindow window) =>
        [.. All<NumericUpDown>(window).Select(n => n.BorderBrush is SolidColorBrush { Color: var c } && c == Colors.Attention)];

    /// <summary>An Auto remap fed by <paramref name="source"/>'s first output, feeding <paramref name="target"/>'s socket <paramref name="port"/> if given.</summary>
    private static (Patch Patch, NodeInstance Remap) Board(string source, string? target = null, int port = 0)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var from = b.Add(source, 40, 40);
        var remap = b.Add(NodeCatalog.AutoRemapTypeId, 360, 40);
        b.Wire(from, 0, remap, AutoRemap.In);

        if (target is not null) b.Wire(remap, 0, b.Add(target, 700, 40), port);

        return (b.Patch, remap);
    }

    [AvaloniaFact]
    public void A_source_with_no_range_outlines_the_input_pair_and_nothing_else()
    {
        var (patch, remap) = Board(NodeCatalog.ValueTypeId);
        var window = Open(patch);

        Select(window, remap);

        Outlined(window).ShouldBe([true, true, false, false]);
    }

    [AvaloniaFact]
    public void A_fraction_reads_as_what_it_comes_to_at_the_socket_it_feeds()
    {
        var (patch, remap) = Board(NodeCatalog.SineTypeId, NodeCatalog.FilterTypeId, 1);
        var window = Open(patch);

        Select(window, remap);

        Outlined(window).ShouldBe([false, false, false, false]);
        All<TextBlock>(window).ShouldContain(t => t.Text == "12000");
    }

    [AvaloniaFact]
    public void Patching_its_output_into_a_socket_with_no_range_outlines_the_output_pair()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var sine = b.Add(NodeCatalog.SineTypeId, 40, 40);
        var remap = b.Add(NodeCatalog.AutoRemapTypeId, 360, 40);
        var add = b.Add("math.add", 700, 40);
        b.Wire(sine, 0, remap, AutoRemap.In);

        var window = Open(b.Patch);

        Select(window, remap);
        Outlined(window).ShouldBe([false, false, false, false]);

        var from = OnWindow(window, NodeGeometry.OutputPort(remap, 0));
        var to = OnWindow(window, NodeGeometry.InputPort(add, NodeCatalog.BuiltIn.Require("math.add"), 0));

        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(to);
        window.MouseUp(to, MouseButton.Left);
        Settle(window);

        Editor(window).Patch.SoleOutgoingFrom(remap.Id, 0).ShouldNotBeNull();
        Outlined(window).ShouldBe([false, false, true, true]);
    }
}
