using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Flyback.Core.Graph;
using Flyback.Editor.Bars;
using Flyback.Plugins.Testing;
using Flyback.Ui.Audio;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Flyback.Editor.Tests.Bars;

/// <summary>The status bar's count of what the patch costs, the picture and the sound each: the sound's oversampling and rungs for how far ahead it renders.</summary>
public sealed class StatusCountTests : EditorTest
{
    [AvaloniaFact]
    public void It_shows_the_oversampling_and_lights_rungs_for_how_fast_a_sound_renders_and_counts_no_modules_or_wires()
    {
        var speakers = new LoopbackDevice();
        var window = Open(Tone(sound: true), replace: services => services.AddSingleton(new AudioSetup(speakers, new LoopbackOutput(speakers))));

        Play(window);

        var bar = Service<StatusBar>(window);
        var count = All<TextBlock>(window).Single(text => text.Name == "statusCount");
        var oversample = All<TextBlock>(window).Single(text => text.Name == "soundOversample");
        var panel = All<StackPanel>(window).Single(panel => panel.Name == "soundHeadroom");

        // Written on a timer, which a headless run cannot be relied on to tick, so the test writes it.
        Pump(() =>
        {
            speakers.Pump();
            bar.Update();
            return bar.Rungs.Lit > 0;
        }, window);

        panel.IsVisible.ShouldBeTrue();

        // A sine at Volume 1 peaks near full scale and never past it.
        Pump(() =>
        {
            speakers.Pump();
            bar.ShowLevel();
            return double.IsFinite(bar.Level.Level);
        }, window);

        bar.Level.IsVisible.ShouldBeTrue();
        bar.Level.Level.ShouldBeGreaterThan(-3);
        bar.Level.ClipLit.ShouldBeFalse();
        (ToolTip.GetTip(bar.Level) as string ?? "").ShouldMatch(@"^Peak -?\d+\.\d dB; no clipping\.$");
        oversample.Text.ShouldBe("2×");
        (ToolTip.GetTip(panel) as string ?? "").ShouldMatch(@"^Sound renders \d+\.\d\d× faster than it plays, with 2× oversampling\.$");
        (count.Text ?? "").ShouldNotContain("oversampling");
        (count.Text ?? "").ShouldNotContain("renders");

        (count.Text ?? "").ShouldNotContain("modules");
        (count.Text ?? "").ShouldNotContain("wires");
        (count.Text ?? "").ShouldNotContain("ops");
    }

    [AvaloniaFact]
    public void It_says_nothing_of_the_sound_of_a_patch_with_no_sound()
    {
        var speakers = new LoopbackDevice();
        var window = Open(Tone(sound: false), replace: services => services.AddSingleton(new AudioSetup(speakers, new LoopbackOutput(speakers))));

        var bar = Service<StatusBar>(window);
        var count = All<TextBlock>(window).Single(text => text.Name == "statusCount");

        for (var i = 0; i < 20; i++)
        {
            // Nothing need have started a device for a patch with no sound.
            if (speakers.IsRunning) speakers.Pump();
            bar.Update();
            Dispatcher.UIThread.RunJobs();
        }

        All<StackPanel>(window).Single(panel => panel.Name == "soundHeadroom").IsVisible.ShouldBeFalse();
        bar.Level.IsVisible.ShouldBeFalse();
        (count.Text ?? "").ShouldNotContain("oversampling");
    }

    /// <summary>
    /// A sine into the speakers with Volume up, or onto the screen alone. The one with sound
    /// has no picture, which a headless preview never draws and so would hold its start.
    /// </summary>
    private static Patch Tone(bool sound)
    {
        var builder = new PatchBuilder();
        var sine = builder.Add("osc.sine", (1, 220f));
        var output = builder.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));

        return builder.Wire(sine, 0, output, sound ? NodeCatalog.OutputLeftPort : NodeCatalog.OutputColorPort).Build();
    }

}
