using Flyback.Core;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Engine.Graph;
using Flyback.Plugins.Audio;
using Flyback.Plugins.Hosting;
using Flyback.Plugins.Settings;
using Flyback.Ui.Audio;
using Flyback.Ui.Midi;
using Shouldly;
using Xunit;

namespace Flyback.Ui.Tests.Audio;

/// <summary>
/// The microphone is held open for exactly as long as the sound that is playing reads
/// it, and what it hears is what the patch plays.
/// </summary>
public class LineInTests
{
    private const int Frames = 2048;

    /// <summary>A sound card the test is the thread of.</summary>
    private sealed class Speakers : IAudioDevice
    {
        private AudioCallback? fill;

        public int SampleRate => GlobalConstants.SampleRate;

        public bool IsRunning => fill is not null;

        public void Start(AudioCallback callback) => fill = callback;

        public void Stop() => fill = null;

        public void Dispose() => Stop();

        public float[] Pump()
        {
            var buffer = new float[Frames * 2];
            fill!(buffer);
            return buffer;
        }
    }

    /// <summary>A microphone the test speaks into.</summary>
    private sealed class Microphone : IAudioCapture
    {
        private AudioCaptureCallback? deliver;

        public int SampleRate => GlobalConstants.SampleRate;

        public int Channels => 2;

        public bool IsRunning { get; set; }

        public int Opened { get; private set; }

        public int Closed { get; private set; }

        public void Start(AudioCaptureCallback callback)
        {
            deliver = callback;
            IsRunning = true;
            Opened++;
        }

        public void Stop()
        {
            IsRunning = false;
            deliver = null;
        }

        public void Dispose()
        {
            Closed++;
            Stop();
        }

        public void Say(float[] interleaved) => deliver!(interleaved);
    }

    private sealed class Backend(Microphone microphone, bool works = true) : IAudioInput
    {
        public bool Works { get; set; } = works;

        public string Id => "fake";

        public string Name => "Fake";

        public int Priority => 0;

        public bool IsSupported => true;

        public IAudioCapture Create(AudioFormat format, SettingValues settings) =>
            Works ? microphone : throw new InvalidOperationException("taken by another program");
    }

    private static Patch ThroughLineIn()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);

        var line = builder.Add(NodeCatalog.LineInTypeId, 0, 0);
        var speaker = builder.Add(NodeCatalog.OutputTypeId, 0, 0, (NodeCatalog.OutputVolumePort, 1f));

        return builder.Wire(line, 0, speaker, NodeCatalog.OutputLeftPort).Patch;
    }

    private static Patch Silent()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);

        builder.Add(NodeCatalog.OutputTypeId, 0, 0, (NodeCatalog.OutputVolumePort, 1f));

        return builder.Patch;
    }

    private static PluginCatalog Catalog(params IAudioInput[] inputs) =>
        new([], [], NodeCatalog.BuiltIn, Presets.All, [], audioInputs: inputs);

    private sealed class Rig : IDisposable
    {
        public Rig(Microphone microphone, bool works = true, bool installed = true)
        {
            Backend = new Backend(microphone, works);
            Engine = new AudioEngine(new AudioSetup(Speakers));
            Line = new LineIn(
                installed ? Catalog(Backend) : Catalog(),
                Engine,
                () => new OutputSettings());
            Line.Trouble += message => Said.Add(message);
        }

        public Speakers Speakers { get; } = new();

        public Backend Backend { get; }

        public AudioEngine Engine { get; }

        public LineIn Line { get; }

        public List<string> Said { get; } = [];

        public void Play(Patch patch)
        {
            Engine.Update(patch);
            Engine.Start();
            Line.Follow();
        }

        public void Dispose()
        {
            Line.Dispose();
            Engine.Dispose();
        }
    }

    [Fact]
    public void A_patch_with_a_line_in_opens_the_microphone_once_it_plays()
    {
        var microphone = new Microphone();
        using var rig = new Rig(microphone);

        rig.Engine.Update(ThroughLineIn());
        rig.Line.Follow();

        microphone.Opened.ShouldBe(0);

        rig.Engine.Start();
        rig.Line.Follow();

        microphone.Opened.ShouldBe(1);
        rig.Line.IsListening.ShouldBeTrue();
    }

    [Fact]
    public void A_patch_without_one_never_opens_it()
    {
        var microphone = new Microphone();
        using var rig = new Rig(microphone);

        rig.Play(Silent());

        microphone.Opened.ShouldBe(0);
    }

    [Fact]
    public void Following_again_does_not_open_it_again()
    {
        var microphone = new Microphone();
        using var rig = new Rig(microphone);

        rig.Play(ThroughLineIn());
        rig.Line.Follow();
        rig.Line.Follow();

        microphone.Opened.ShouldBe(1);
    }

    [Fact]
    public void Stopping_the_sound_lets_the_microphone_go()
    {
        var microphone = new Microphone();
        using var rig = new Rig(microphone);

        rig.Play(ThroughLineIn());
        rig.Engine.Stop();
        rig.Line.Follow();

        microphone.Closed.ShouldBe(1);
        rig.Line.IsListening.ShouldBeFalse();
    }

    [Fact]
    public void Taking_the_line_in_out_of_the_patch_lets_the_microphone_go()
    {
        var microphone = new Microphone();
        using var rig = new Rig(microphone);

        rig.Play(ThroughLineIn());
        rig.Engine.Update(Silent());
        rig.Line.Follow();

        microphone.Closed.ShouldBe(1);
    }

    [Fact]
    public void What_the_microphone_hears_is_what_the_patch_plays()
    {
        var microphone = new Microphone();
        using var rig = new Rig(microphone);

        rig.Play(ThroughLineIn());

        var said = new float[Frames * 2];

        for (var i = 0; i < Frames; i++)
            said[i * 2] = said[i * 2 + 1] = 0.5f * MathF.Sin(MathF.Tau * 1000f * i / GlobalConstants.SampleRate);

        microphone.Say(said);

        var played = rig.Speakers.Pump();
        var sum = 0d;

        for (var frame = 500; frame < Frames; frame++) sum += played[frame * 2] * played[frame * 2];

        Math.Sqrt(sum / (Frames - 500)).ShouldBe(0.5 / Math.Sqrt(2), 0.03);
    }

    [Fact]
    public void A_microphone_that_will_not_open_is_said_once_and_left_alone()
    {
        using var rig = new Rig(new Microphone(), works: false);

        rig.Play(ThroughLineIn());
        rig.Line.Follow();
        rig.Line.Follow();

        rig.Said.ShouldHaveSingleItem().ShouldContain("taken by another program");
        rig.Line.IsListening.ShouldBeFalse();
    }

    [Fact]
    public void Saving_the_settings_tries_a_refused_microphone_again()
    {
        var microphone = new Microphone();
        using var rig = new Rig(microphone, works: false);

        rig.Play(ThroughLineIn());
        rig.Said.Count.ShouldBe(1);

        rig.Backend.Works = true;
        rig.Line.Follow();

        microphone.Opened.ShouldBe(0);

        rig.Line.Reconfigure();

        microphone.Opened.ShouldBe(1);
        rig.Line.IsListening.ShouldBeTrue();
    }

    [Fact]
    public void A_machine_with_no_sound_input_says_so_and_stays_silent()
    {
        using var rig = new Rig(new Microphone(), installed: false);

        rig.Play(ThroughLineIn());

        rig.Said.ShouldHaveSingleItem().ShouldContain("no sound input");
        rig.Speakers.Pump().ShouldAllBe(sample => sample == 0f);
    }

    [Fact]
    public void A_microphone_that_stops_on_its_own_is_said_and_not_reopened()
    {
        var microphone = new Microphone();
        using var rig = new Rig(microphone);

        rig.Play(ThroughLineIn());
        microphone.IsRunning = false;

        rig.Line.Follow();
        rig.Line.Follow();

        rig.Said.ShouldHaveSingleItem().ShouldContain("stopped listening");
        microphone.Opened.ShouldBe(1);
    }

    /// <summary>The shells start and stop the sound through the transport, so it is what opens and closes the microphone.</summary>
    [Fact]
    public void The_transport_listens_while_it_plays_and_lets_go_when_it_stops()
    {
        var microphone = new Microphone();
        using var rig = new Rig(microphone);
        using var compiler = new IlCompiler();
        using var midi = new MidiHub(Catalog().PreferredMidiInput);
        var transport = new Transport(rig.Engine, null, compiler, midi, rig.Line);

        transport.Load(ThroughLineIn(), null, null, null);
        microphone.Opened.ShouldBe(0);

        transport.Start();
        microphone.Opened.ShouldBe(1);

        transport.Stop();
        microphone.Closed.ShouldBe(1);
    }
}
