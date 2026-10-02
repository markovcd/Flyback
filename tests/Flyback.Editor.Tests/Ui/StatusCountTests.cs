using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Flyback.Engine.Compile;
using Flyback.Ui.Audio;
using Flyback.Editor.Bars;
using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins.Audio;
using Flyback.Plugins.Settings;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Flyback.Editor.Tests.Ui;

/// <summary>The status bar's count of what the patch costs, the picture and the sound each, the sound's oversampling and how fast it renders.</summary>
public sealed class StatusCountTests : EditorTest
{
    /// <summary>Written on a timer, which a headless run cannot be relied on to tick, so the test writes it.</summary>
    private static bool Until(Func<bool> done, double seconds = 10)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);

        while (!done() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }

        return done();
    }

    [AvaloniaFact]
    public void It_counts_the_ops_of_the_picture_and_of_the_sound()
    {
        var window = NewMainWindow();
        window.Show();
        Settle(window);

        var bar = Service<StatusBar>(window);
        var count = All<TextBlock>(window).Single(text => text.Name == "statusCount");

        Until(() =>
        {
            bar.Update();
            return count.Text?.Contains(Counted(Editor(window).History.Patch)) == true;
        })
            .ShouldBeTrue($"{count.Text} against {Counted(Editor(window).History.Patch)}");
    }

    [AvaloniaFact]
    public void It_says_how_fast_a_sound_renders_and_counts_no_modules_or_wires()
    {
        var speakers = new Loopback();
        var window = Open(Tone(sound: true), replace: services => services.AddSingleton(new AudioSetup(speakers, new Plug(speakers))));

        Play(window);

        var bar = Service<StatusBar>(window);
        var count = All<TextBlock>(window).Single(text => text.Name == "statusCount");

        Until(() =>
        {
            speakers.Pull();
            bar.Update();
            return count.Text?.Contains("sound renders at ") == true;
        })
            .ShouldBeTrue(count.Text);

        (count.Text ?? "").ShouldContain("picture/sound ops · 2× oversampling · sound renders at ");

        (count.Text ?? "").ShouldNotContain("modules");
        (count.Text ?? "").ShouldNotContain("wires");
    }

    [AvaloniaFact]
    public void It_says_nothing_of_the_sound_of_a_patch_with_no_sound()
    {
        var speakers = new Loopback();
        var window = Open(Tone(sound: false), replace: services => services.AddSingleton(new AudioSetup(speakers, new Plug(speakers))));

        var bar = Service<StatusBar>(window);
        var count = All<TextBlock>(window).Single(text => text.Name == "statusCount");

        for (var i = 0; i < 20; i++)
        {
            speakers.Pull();
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

    /// <summary>A sound card the test plays by pulling buffers from it, once the editor has started it.</summary>
    private sealed class Loopback : IAudioDevice
    {
        private AudioCallback? fill;

        public int SampleRate => GlobalConstants.SampleRate;

        public bool IsRunning => fill is not null;

        public void Start(AudioCallback callback) => fill = callback;

        public void Stop() => fill = null;

        public void Dispose() => Stop();

        public void Pull() => fill?.Invoke(new float[1024]);
    }

    private static string Counted(Patch patch) =>
        $"{patch.CompileForVideo(played: true).Program.Ops.Length}/"
        + $"{patch.CompileForAudio(played: true).Program.Ops.Length} picture/sound ops";

    /// <summary>The output the loopback came from, which is what lets the editor start it.</summary>
    private sealed class Plug(IAudioDevice device) : IAudioOutput
    {
        public string Id => "loopback";

        public string Name => "Loopback";

        public int Priority => 0;

        public bool IsSupported => true;

        public IAudioDevice Create(AudioFormat format, SettingValues settings) => device;
    }
}
