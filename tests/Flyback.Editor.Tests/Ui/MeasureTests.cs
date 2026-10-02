using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Flyback.Core.Graph;
using Flyback.Editor.Canvas;
using Flyback.Editor.Windows;
using Shouldly;

namespace Flyback.Editor.Tests.Ui;

/// <summary>
/// Measure (Ctrl+M) runs the patch for a few seconds and pins what each output carries
/// beside it and under its row in the inspector, until an edit makes it out of date.
/// </summary>
public class MeasureTests : EditorTest
{
    private MainWindow Opened(out NodeInstance lfo, out NodeInstance coords)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        lfo = b.Add("osc.sine", 0, 0, (1, 3f));
        coords = b.Add("coord", 0, 400);
        b.Add(NodeCatalog.OutputTypeId, 700, 0);

        var window = NewMainWindow();

        window.Show();
        Settle(window);

        Editor(window).History.Open(b.Patch);
        Settle(window);

        return window;
    }

    private void Measure(MainWindow window)
    {
        window.KeyPress(Key.M, RawInputModifiers.Control, PhysicalKey.M, "m");
        Pump(() => Service<MeasureLabels>(window).Report is not null && !Service<Measuring>(window).Running, window);
    }

    [AvaloniaFact]
    public void Ctrl_M_pins_every_output_with_nothing_selected()
    {
        var window = Opened(out var lfo, out var coords);

        Measure(window);

        var labels = Service<MeasureLabels>(window);

        labels.Of(lfo.Id, 0)!.Sound.Single().Hz!.Value.ShouldBe(3d, 0.05d);
        labels.Of(coords.Id, 0)!.Picture.Single().Across.ShouldBeTrue();
        labels.Stale.ShouldBeFalse();
    }

    [AvaloniaFact]
    public void With_a_module_selected_only_its_outputs_are_measured()
    {
        var window = Opened(out var lfo, out var coords);

        Click(Editor(window), window, lfo);
        Measure(window);

        var labels = Service<MeasureLabels>(window);

        labels.Of(lfo.Id, 0).ShouldNotBeNull();
        labels.Of(coords.Id, 0).ShouldBeNull();
    }

    [AvaloniaFact]
    public void The_inspector_shows_the_selected_modules_measurement_under_its_output()
    {
        var window = Opened(out var lfo, out _);

        Click(Editor(window), window, lfo);
        Measure(window);
        Settle(window);

        All<TextBlock>(window).Single(t => t.Name == "measurement").Text!.ShouldContain("3 Hz");
    }

    [AvaloniaFact]
    public void An_edit_marks_the_measurement_out_of_date()
    {
        var window = Opened(out var lfo, out _);

        Measure(window);

        var editor = Editor(window);
        editor.History.Patch.Find(lfo.Id)!.InputValues[1] = 5f;
        editor.History.Record();
        Settle(window);

        Service<MeasureLabels>(window).Stale.ShouldBeTrue();
    }

    [AvaloniaFact]
    public void A_shut_box_shows_only_what_is_on_its_edge_and_a_peek_shows_the_rest()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var inner = b.Add("osc.sine", 0, 0, (1, 3f));
        var edge = b.Add("osc.sine", 300, 0);
        var sink = b.Add(NodeCatalog.OutputTypeId, 800, 0);
        b.Wire(inner, 0, edge, 1).Wire(edge, 0, sink, NodeCatalog.OutputLeftPort);

        var group = b.Patch.Group([inner.Id, edge.Id]).ShouldNotBeNull();
        group.Collapsed = true;

        var window = NewMainWindow();
        window.Show();
        Settle(window);
        Editor(window).History.Open(b.Patch);
        Settle(window);

        Editor(window).Selection.Select(null);
        Measure(window);

        var labels = Service<MeasureLabels>(window);
        var scene = Editor(window).Selection.Scene;
        var shown = labels.Shown(peeked: false).ToList();

        shown.ShouldNotContain(label => label.Measured.Node == inner.Id);
        var onEdge = shown.Single(label => label.Measured.Node == edge.Id);
        onEdge.Area.Left.ShouldBeGreaterThan(scene.OutputAnchor(edge, 0).X);
        scene.OutputAnchor(edge, 0).ShouldNotBe(NodeGeometry.OutputPort(edge, 0));

        Editor(window).Selection.Peek(Editor(window).History.Patch.Groups!.Single());
        Settle(window);

        labels.Shown(peeked: true).Select(label => label.Measured.Node).ShouldBe([inner.Id, edge.Id], ignoreOrder: true);
        labels.Shown(peeked: false).ShouldBeEmpty();
    }

    [AvaloniaFact]
    public void Measuring_again_hides_fresh_labels_and_measures_afresh_after_an_edit()
    {
        var window = Opened(out var lfo, out _);
        var labels = Service<MeasureLabels>(window);

        Measure(window);

        window.KeyPress(Key.M, RawInputModifiers.Control, PhysicalKey.M, "m");
        Settle(window);

        labels.Report.ShouldBeNull();
        All<TextBlock>(window).ShouldNotContain(t => t.Name == "measurement");

        Measure(window);

        var editor = Editor(window);
        editor.History.Patch.Find(lfo.Id)!.InputValues[1] = 5f;
        editor.History.Record();
        Settle(window);

        Measure(window);

        labels.Stale.ShouldBeFalse();
        labels.Of(lfo.Id, 0)!.Sound.Single().Hz!.Value.ShouldBe(5d, 0.05d);
    }

    [AvaloniaFact]
    public void Settings_say_how_long_a_measurement_runs()
    {
        var window = Opened(out _, out _);
        var section = Service<CanvasSection>(window);

        All<ComboBox>(section.View).Single(box => box.Name == "measureWindow").SelectedIndex =
            CanvasSettings.MeasureWindows.ToList().IndexOf(1);
        section.Save();

        Measure(window);

        Service<MeasureLabels>(window).Report!.Seconds.ShouldBe(1d);
    }
}
