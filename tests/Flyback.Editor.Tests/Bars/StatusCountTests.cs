using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Flyback.Ui.Audio;
using Flyback.Editor.Bars;
using Flyback.Core;
using Flyback.Core.Graph;
using Flyback.Plugins.Audio;
using Flyback.Plugins.Settings;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Flyback.Plugins.Testing;

namespace Flyback.Editor.Tests.Bars;

/// <summary>The status bar's count of what the patch costs, the picture and the sound each, the sound's oversampling and how fast it renders.</summary>
public sealed class StatusCountTests : EditorTest
{
    [AvaloniaFact]
    public void It_says_how_fast_a_sound_renders_and_counts_no_modules_or_wires()
    {
        var speakers = new LoopbackDevice();
        var window = Open(Tone(sound: true), replace: services => services.AddSingleton(new AudioSetup(speakers, new LoopbackOutput(speakers))));

        Play(window);

        var bar = Service<StatusBar>(window);
        var count = All<TextBlock>(window).Single(text => text.Name == "statusCount");

        // Written on a timer, which a headless run cannot be relied on to tick, so the test writes it.
        Pump(() =>
        {
            speakers.Pump();
            bar.Update();
            return count.Text?.Contains("sound renders at ") == true;
        }, window);

        (count.Text ?? "").ShouldContain("2× oversampling · sound renders at ");

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

        (count.Text ?? "").ShouldNotContain("sound renders at");
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
