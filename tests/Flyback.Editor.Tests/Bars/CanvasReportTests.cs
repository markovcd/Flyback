using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Input;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Editor.Tests.Bars;

/// <summary>What the canvas says of an edit reaches the window's report line.</summary>
public sealed class CanvasReportTests : EditorTest
{
    [AvaloniaFact]
    public void What_the_canvas_says_is_on_the_report_line()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        var first = builder.Add("osc.sine", 300, 0);
        var sink = builder.Add(NodeCatalog.OutputTypeId, 900, 0);
        builder.Wire(first, 0, sink, NodeCatalog.OutputLeftPort);

        var window = Open(builder.Patch);
        var editor = Editor(window);

        Click(editor, window, Body(first));
        window.KeyPressQwerty(PhysicalKey.G, RawInputModifiers.Control);
        Settle(window);

        var report = All<ReportLine>(window).Single();
        report.History.ShouldContain(message => message.Contains("needs 2 modules"), "the canvas declined the group, and the report line should say so");
    }
}
