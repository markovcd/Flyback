using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Flyback.Core.Graph;
using Flyback.Editor.Canvas;
using Flyback.Editor.Composition;
using Flyback.Editor.Controls;
using Flyback.Editor.Shell;
using Flyback.Engine.Mix;
using Shouldly;

namespace Flyback.Editor.Tests.Canvas;

/// <summary>
/// Check the Mix (Ctrl+Shift+H) listens to the patch from the playhead and shows what it
/// found in a panel; only on a desktop, since it renders the sound twice over.
/// </summary>
public sealed class MixCheckingTests : EditorTest
{
    private MainWindow Open(bool onDevice = false)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var tone = b.Add("osc.sine", 0, 0, (1, 220f));
        var sink = b.Add(OutputModule.TypeId, 700, 0);
        b.Wire(tone, 0, sink, OutputModule.LeftPort);

        var window = NewMainWindow(new EditorSetup { Host = new() { Place = onDevice ? EditorPlace.Device : EditorPlace.Desktop } });

        window.Show();
        Settle(window);

        Editor(window).History.Open(b.Patch);
        Settle(window);

        return window;
    }

    [AvaloniaFact]
    public async Task A_check_on_a_desktop_says_what_it_found()
    {
        var window = Open();

        var report = (await Service<MixChecking>(window).CheckAsync(seconds: 0.5)).ShouldNotBeNull();

        report.Findings.ShouldContain(f => f.Problem == MixProblem.NoMasterChain);
        report.Figures.TruePeak.ShouldBeGreaterThan(-10d);
    }

    [AvaloniaFact]
    public void Ctrl_Shift_H_shows_the_panel()
    {
        var window = Open();

        window.KeyPress(Key.H, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.H, "H");
        Pump(() => All<StackPanel>(window).Any(panel => panel.Name == "mixCheck"), window);

        Service<IDialog>(window).IsShowing.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task A_device_checks_nothing()
    {
        var window = Open(onDevice: true);

        (await Service<MixChecking>(window).CheckAsync()).ShouldBeNull();
        Service<MixChecking>(window).Running.ShouldBeFalse();
    }
}
