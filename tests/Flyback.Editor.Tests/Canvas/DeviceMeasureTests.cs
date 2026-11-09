using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Flyback.Core.Graph;
using Flyback.Editor.Canvas;
using Flyback.Editor.Composition;
using Flyback.Editor.Inspect;
using Flyback.Editor.Shell;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Tests.Canvas;

/// <summary>A phone or tablet offers no Measure: no button, no shortcut, no settings and no help row.</summary>
public sealed class DeviceMeasureTests : EditorTest
{
    private MainWindow Open(bool onDevice)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        b.Add("osc.sine", 0, 0, (1, 3f));
        b.Add(NodeCatalog.OutputTypeId, 700, 0);

        var window = NewMainWindow(new EditorSetup { Host = new() { OnDevice = onDevice } });

        window.Show();
        Settle(window);

        Editor(window).History.Open(b.Patch);
        Settle(window);

        return window;
    }

    [AvaloniaTheory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public void Only_a_desktop_has_the_measure_button(bool onDevice, int buttons) =>
        All<Button>(Open(onDevice)).Count(b => b.Name == "measure").ShouldBe(buttons);

    [AvaloniaFact]
    public void Ctrl_Shift_M_measures_nothing_on_a_device()
    {
        var window = Open(onDevice: true);

        window.KeyPress(Key.M, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.M, "M");
        Settle(window);

        Service<MeasureLabels>(window).Report.ShouldBeNull();
        Service<Measuring>(window).Running.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Measuring_on_a_device_is_refused()
    {
        var window = Open(onDevice: true);

        (await Service<Measuring>(window).MeasureAsync()).ShouldBeNull();
        Service<MeasureLabels>(window).Report.ShouldBeNull();
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_help_leaves_out_Measure_where_it_is_not_offered(bool fingers)
    {
        Titles(InspectorHelp.Shortcuts(inPage: false, fingers, measures: true)).ShouldContain("Measure");
        Titles(InspectorHelp.Shortcuts(inPage: false, fingers, measures: false)).ShouldNotContain("Measure");
        Titles(InspectorHelp.Shortcuts(inPage: false, fingers, measures: false)).ShouldContain("Record");
    }

    private static string[] Titles(IReadOnlyList<HelpGroup> groups) => [.. groups.SelectMany(g => g.Rows).Select(r => r.Title)];
}
