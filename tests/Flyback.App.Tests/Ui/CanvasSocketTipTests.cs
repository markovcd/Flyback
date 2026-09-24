using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Flyback.App.Controls;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.App.Tests.Ui;

/// <summary>Hovering a socket on the canvas says what it is for, in the panel's words.</summary>
public class CanvasSocketTipTests : UiTest
{
    private static readonly NodeDef Filter = NodeCatalog.BuiltIn.Require(NodeCatalog.FilterTypeId);

    [AvaloniaFact]
    public void Hovering_an_input_socket_shows_its_help()
    {
        var window = NewMainWindow();

        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        b.Add(NodeCatalog.OutputTypeId, 900, 40);
        var module = b.Add(NodeCatalog.FilterTypeId, 300, 100);

        var editor = All<NodeEditor>(window).Single();

        editor.History.Open(b.Patch);
        Settle(window);

        var port = Geometry.InputPort(module, Filter, 1);
        var at = editor.TranslatePoint(editor.GraphToScreen.Transform(port), window)!.Value;

        window.MouseMove(at);
        Settle(window);

        ToolTip.GetTip(editor).ShouldBe(Filter.Inputs[1].Help);

        window.MouseMove(new Point(at.X - 60, at.Y - 60));
        Settle(window);

        ToolTip.GetTip(editor).ShouldBeNull();
    }
}
