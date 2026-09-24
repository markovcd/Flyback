using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>
/// Compact modules: an input and an output share each row, and an unwired input's
/// value is in its tooltip rather than on the row.
/// </summary>
/// <remarks>
/// <see cref="NodeGeometry.Compact"/> is one switch for the whole app, so every test
/// here runs on the UI thread, which serializes it against the other canvas tests,
/// and puts it back before it returns.
/// </remarks>
public class CompactModuleTests : UiTest
{
    private static readonly NodeDef Filter = NodeCatalog.BuiltIn.Require(NodeCatalog.FilterTypeId);

    [AvaloniaFact]
    public void A_compact_module_is_as_tall_as_its_longer_side()
    {
        var node = NodeInstance.Create(Filter, 0, 0);
        var tall = NodeGeometry.Height(Filter);

        try
        {
            NodeGeometry.Compact = true;

            NodeGeometry.Height(Filter).ShouldBeLessThan(tall);
            NodeGeometry.Height(Filter).ShouldBe(NodeGeometry.Metrics.Height(Filter));
            NodeGeometry.InputPort(node, Filter, 0).Y.ShouldBe(NodeGeometry.OutputPort(node, 0).Y);

            for (var port = 0; port < Filter.Inputs.Count; port++)
                NodeGeometry.Metrics.InputPort(Filter, port).ShouldBe(NodeGeometry.InputPort(node, Filter, port).Y);
        }
        finally
        {
            NodeGeometry.Compact = false;
        }
    }

    [AvaloniaFact]
    public void Hovering_a_compact_input_row_shows_its_value_then_its_help()
    {
        try
        {
            var window = NewMainWindow();

            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            // After the window, which puts the switch where its settings say.
            NodeGeometry.Compact = true;

            var b = new PatchBuilder(NodeCatalog.BuiltIn);

            b.Add(NodeCatalog.OutputTypeId, 900, 40);
            var module = b.Add(NodeCatalog.FilterTypeId, 300, 100);

            var editor = All<NodeEditor>(window).Single();

            editor.History.Open(b.Patch);
            Settle(window);

            // On the row's name rather than its socket, which is where a value used to be read.
            var row = NodeGeometry.InputPort(module, Filter, 1) + new Point(30, 0);
            var at = editor.TranslatePoint(editor.GraphToScreen.Transform(row), window)!.Value;

            window.MouseMove(at);
            Settle(window);

            ToolTip.GetTip(editor).ShouldBe(
                $"{Filter.Inputs[1].Format(module.InputValues[1])}\n{Filter.Inputs[1].Help}");
        }
        finally
        {
            NodeGeometry.Compact = false;
        }
    }
}
