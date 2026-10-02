using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// The Easy Synth: it sounds with nothing wired, plays the note it is given, and
/// no knob, wire or setting takes it past full scale.
/// </summary>
public class EasySynthTests
{
    private const string Synth = "flyback.easy.synth";

    private const int Pitch = 1, Gate = 2, Velocity = 3, Octave = 4, Glide = 5, Sub = 6, Noise = 7;
    private const int Attack = 8, Decay = 9, Sustain = 10, Release = 11, Bright = 12, Resonance = 13, Sweep = 14;
    private const int Lfo1Rate = 15, Lfo1Depth = 16, Lfo2Rate = 17, Lfo2Depth = 18, Drive = 19, Pan = 20;

    private const int Left = 0, Right = 1, Env = 2, Lfo = 3;

    private const int Rate = GlobalConstants.SampleRate;

    private static readonly string[] Waves = ["sine", "triangle", "saw", "square", "pulse", "supersaw", "organ"];
    private static readonly string[] Filters = ["low", "band", "high", "off"];
    private static readonly string[] Shapes = ["sine", "triangle", "square", "ramp", "random"];
    private static readonly string[] Targets = ["pitch", "filter", "volume", "pan"];

    private static ModuleCatalog Catalog => ShippedPlugins.Loaded.Modules;

    public static TheoryData<string, string> EveryWaveAndFilter
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var wave in Waves)
            foreach (var filter in Filters)
                data.Add(wave, filter);

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(EveryWaveAndFilter))]
    public void Left_alone_it_sounds_and_stays_on_the_rails(string wave, string filter)
    {
        var (left, right) = Played(Module(("wave", wave), ("filter", filter)), 0.5);

        Rms(left).ShouldBeGreaterThan(0.05, "it should drone with nothing wired");

        foreach (var sample in left.Concat(right)) sample.ShouldBeInRange(-1f, 1f);
    }

    /// <summary>Every knob and every setting at random, many times over, and still never past full scale.</summary>
    [Fact]
    public void No_knob_or_setting_takes_it_past_full_scale()
    {
        var random = new Random(7);
        var inputs = Catalog.Require(Synth).Inputs;

        for (var trial = 0; trial < 60; trial++)
        {
            var node = Module(
                ("wave", Pick(Waves)), ("filter", Pick(Filters)),
                ("lfo1_shape", Pick(Shapes)), ("lfo1_to", Pick(Targets)),
                ("lfo2_shape", Pick(Shapes)), ("lfo2_to", Pick(Targets)));

            // Past both ends of every knob, the way a wire can be.
            for (var port = 1; port < inputs.Count; port++)
            {
                var spec = inputs[port];
                var span = spec.Max - spec.Min;
                node.InputValues[port] = spec.Min - span + (float)random.NextDouble() * span * 3f;
            }

            var (left, right) = Played(node, 0.1);

            foreach (var sample in left.Concat(right))
            {
                float.IsFinite(sample).ShouldBeTrue($"trial {trial}");
                sample.ShouldBeInRange(-1f, 1f, $"trial {trial}");
            }
        }

        string Pick(string[] from) => from[random.Next(from.Length)];
    }

    [Theory]
    [InlineData(69f, 0f, 440.0)]
    [InlineData(57f, 0f, 220.0)]
    [InlineData(57f, 1f, 440.0)]
    [InlineData(60f, -1f, 130.81)]
    public void It_plays_the_note_it_is_given(float note, float octave, double hertz)
    {
        var node = Module(("wave", "sine"), ("filter", "off"));
        node.InputValues[Pitch] = note;
        node.InputValues[Octave] = octave;

        var (left, _) = Played(node, 1.0);

        Crossings(left.AsSpan(Rate / 2)).ShouldBe(hertz, hertz * 0.01);
    }

    [Fact]
    public void Once_the_gate_drops_it_fades_to_silence_over_its_release()
    {
        var node = Module();
        node.InputValues[Gate] = 0f;
        node.InputValues[Release] = -1f;

        var (left, _) = Played(node, 0.5);

        Rms(left.AsSpan(Rate / 4)).ShouldBeLessThan(1e-4);
    }

    [Fact]
    public void Its_envelope_rises_to_the_peak_and_settles_on_the_sustain()
    {
        var node = Module();
        node.InputValues[Sustain] = 0.3f;

        var env = Played(node, 1.0, Env).Left;

        env.Max().ShouldBe(1f, 1e-3f);
        env[^1].ShouldBe(0.3f, 1e-3f);
    }

    [Fact]
    public void Wired_alone_both_ears_hear_the_same_unless_it_is_panned_or_a_supersaw()
    {
        var (left, right) = Played(Module(("wave", "saw")), 0.2);
        left.ShouldBe(right);

        var (wideLeft, wideRight) = Played(Module(("wave", "supersaw")), 0.2);
        Rms(Difference(wideLeft, wideRight)).ShouldBeGreaterThan(0.01);

        var panned = Module(("wave", "saw"));
        panned.InputValues[Pan] = -1f;
        var (hardLeft, silentRight) = Played(panned, 0.2);

        Rms(hardLeft).ShouldBeGreaterThan(0.05);
        Rms(silentRight).ShouldBeLessThan(1e-6);
    }

    [Theory]
    [InlineData("pulse")]
    [InlineData("square")]
    [InlineData("saw")]
    [InlineData("organ")]
    public void No_wave_leans_to_one_side(string wave)
    {
        var node = Module(("wave", wave), ("filter", "off"));
        node.InputValues[Sub] = 1f;

        var (left, _) = Played(node, 1.0);

        Math.Abs(left.AsSpan(Rate / 2).ToArray().Average(s => (double)s)).ShouldBeLessThan(0.02);
    }

    /// <summary>Glide is the note sliding: a quarter of the way through a half-second glide it is somewhere between.</summary>
    [Fact]
    public void With_glide_a_new_note_slides_in()
    {
        var b = new PatchBuilder(Catalog);
        var step = b.Add(NodeCatalog.SawTypeId, (1, 1f), (3, 6f), (4, 63f));
        var synth = Configure(b.Add(Synth, (Glide, 0.5f)), ("wave", "sine"), ("filter", "off"));
        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));

        // Up from 57 to 69 over each second, then straight back down.
        b.Wire(step, 0, synth, Pitch).Wire(synth, Left, output, NodeCatalog.OutputLeftPort);

        var (left, _) = Played(b.Build(), 1.2);

        // Just after the jump down from 69 to 57, it is still well above 220 Hz.
        Crossings(left.AsSpan(Rate + Rate / 40, Rate / 20)).ShouldBeGreaterThan(300);
    }

    [Fact]
    public void Its_lfo_output_swings_within_one_either_way_at_its_rate()
    {
        foreach (var shape in Shapes)
        {
            var node = Module(("lfo1_shape", shape));
            node.InputValues[Lfo1Rate] = 10f;

            var lfo = Played(node, 1.0, Lfo).Left;

            foreach (var sample in lfo) sample.ShouldBeInRange(-1f, 1f, shape);
            lfo.Max().ShouldBeGreaterThan(0f, shape);
            lfo.Min().ShouldBeLessThan(0f, shape);
        }
    }

    /// <summary>On the screen there is nothing to remember, so the envelope is the gate and the wave is unfiltered.</summary>
    [Fact]
    public void On_the_screen_the_envelope_is_the_gate()
    {
        var node = Module();
        node.InputValues[Gate] = 1f;

        var program = Wired(node, Env, NodeCatalog.OutputColorPort).CompileForVideo(Catalog).Program;
        var registers = program.AllocateRegisters();

        program.Evaluate(0.3, -0.2, 1.5, registers, default);

        registers[program.OutputBase].ShouldBe(1d, 1e-6);
    }

    [Theory]
    [MemberData(nameof(EveryWaveAndFilter))]
    public void Every_wave_lowers_to_the_screen(string wave, string filter)
    {
        var program = Wired(Module(("wave", wave), ("filter", filter), ("lfo2_shape", "random")), Left, NodeCatalog.OutputColorPort)
            .CompileForVideo(Catalog).Program;

        foreach (var dialect in Enum.GetValues<GlslDialect>())
            GlslEmitter.Emit(program, dialect).PatchFragment.ShouldNotBeNullOrEmpty();
    }

    private static NodeInstance Module(params (string Key, string Value)[] settings) =>
        Configure(NodeInstance.Create(Catalog.Require(Synth), 0, 0), settings);

    private static NodeInstance Configure(NodeInstance node, params (string Key, string Value)[] settings)
    {
        var state = new System.Text.Json.Nodes.JsonObject();
        foreach (var (key, value) in settings) state[key] = value;

        node.SetState("synth", state);

        return node;
    }

    private static Patch Wired(NodeInstance node, int port, int into)
    {
        var b = new PatchBuilder(Catalog);
        b.Patch.Nodes.Add(node);

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));
        b.Wire(node, port, output, into);

        return b.Build();
    }

    /// <summary><paramref name="seconds"/> of <paramref name="port"/> on the left, and 'right' on the right.</summary>
    private static (float[] Left, float[] Right) Played(NodeInstance node, double seconds, int port = Left)
    {
        var b = new PatchBuilder(Catalog);
        b.Patch.Nodes.Add(node);

        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));
        b.Wire(node, port, output, NodeCatalog.OutputLeftPort).Wire(node, Right, output, NodeCatalog.OutputRightPort);

        return Played(b.Build(), seconds);
    }

    private static (float[] Left, float[] Right) Played(Patch patch, double seconds)
    {
        var program = patch.CompileForAudio(Catalog).Program;
        var state = new DelayState(program, Rate);
        var registers = program.AllocateRegisters();

        var left = new float[(int)(seconds * Rate)];
        var right = new float[left.Length];

        for (var i = 0; i < left.Length; i++)
        {
            program.Evaluate(0d, 0d, i / (double)Rate, registers, default, state);
            left[i] = (float)registers[program.OutputBase];
            right[i] = (float)registers[program.OutputBase + 1];
        }

        return (left, right);
    }

    /// <summary>Rising zero crossings a second.</summary>
    private static double Crossings(ReadOnlySpan<float> sound)
    {
        var count = 0;
        for (var i = 1; i < sound.Length; i++)
            if (sound[i - 1] < 0f && sound[i] >= 0f) count++;

        return count * (double)Rate / sound.Length;
    }

    private static double Rms(ReadOnlySpan<float> sound)
    {
        var sum = 0d;
        foreach (var sample in sound) sum += sample * (double)sample;

        return Math.Sqrt(sum / Math.Max(sound.Length, 1));
    }

    private static double Rms(float[] sound) => Rms(sound.AsSpan());

    private static float[] Difference(float[] a, float[] b) => [.. a.Zip(b, (x, y) => x - y)];
}
