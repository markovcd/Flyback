using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Flyback.App.Canvas;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// Compact modules: an input and an output share each row, and an unwired input's
/// value is in its tooltip rather than on the row. The switch is each window's own.
/// </summary>
public class CompactModuleTests : UiTest
{
    private static readonly NodeDef Filter = NodeCatalog.BuiltIn.Require(NodeCatalog.FilterTypeId);

    [AvaloniaFact]
    public void A_compact_module_is_as_tall_as_its_longer_side()
    {
        var node = NodeInstance.Create(Filter, 0, 0);
        var compact = new NodeGeometry { Compact = true };

        compact.Height(Filter).ShouldBeLessThan(Geometry.Height(Filter));
        compact.Height(Filter).ShouldBe(compact.Metrics.Height(Filter));
        compact.InputPort(node, Filter, 0).Y.ShouldBe(NodeGeometry.OutputPort(node, 0).Y);

        for (var port = 0; port < Filter.Inputs.Count; port++)
            compact.Metrics.InputPort(Filter, port).ShouldBe(compact.InputPort(node, Filter, port).Y);
    }

    [AvaloniaFact]
    public void Hovering_a_compact_input_row_shows_its_value_then_its_help()
    {
        var window = NewMainWindow();

        window.Show();
        Settle(window);

        var editor = Editor(window);
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        b.Add(NodeCatalog.OutputTypeId, 900, 40);
        var module = b.Add(NodeCatalog.FilterTypeId, 300, 100);

        editor.Geometry.Compact = true;
        editor.History.Open(b.Patch);
        Settle(window);

        // On the row's name rather than its socket.
        var row = editor.Geometry.InputPort(module, Filter, 1) + new Point(30, 0);
        var at = editor.TranslatePoint(editor.GraphToScreen.Transform(row), window)!.Value;

        window.MouseMove(at);
        Settle(window);

        ToolTip.GetTip(editor).ShouldBe(
            $"{Filter.Inputs[1].Format(module.InputValues[1])}\n{Filter.Inputs[1].Help}");
    }

    [AvaloniaFact]
    public void One_window_drawn_compact_leaves_another_drawn_in_full()
    {
        var compact = Editor(Open());
        var full = Editor(Open());

        compact.Geometry.Compact = true;

        full.Geometry.Compact.ShouldBeFalse();
        full.Geometry.Height(Filter).ShouldBeGreaterThan(compact.Geometry.Height(Filter));
    }
}
