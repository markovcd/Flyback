using Flyback.Core;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// The Easy Drum: it plays in time with nothing wired, each sound in the rhythm that
/// suits it, and no knob, wire or setting takes it past full scale.
/// </summary>
public class EasyDrumTests
{
    private const string Drum = "flyback.easy.drummer";

    private const int Bpm = 1, Swing = 2, Trigger = 3, Velocity = 4, Tune = 5, Decay = 6, Tone = 7, Drive = 8, Pan = 9;

    private const int Left = 0, Right = 1, Env = 2;

    private const int Rate = GlobalConstants.SampleRate;

    private static readonly string[] Sounds = ["kick", "psy kick", "snare", "clap", "closed hat", "open hat", "tom", "rim", "cowbell"];

    private static readonly string[] Rhythms =
        ["auto", "beats", "backbeat", "eighths", "sixteenths", "offbeats", "tresillo", "clave", "bar", "trigger"];

    private static ModuleCatalog Catalog => ShippedPlugins.Loaded.Modules;

    public static TheoryData<string> EverySound => [.. Sounds];

    [Theory]
    [MemberData(nameof(EverySound))]
    public void Left_alone_it_plays_and_stays_on_the_rails(string sound)
    {
        var (left, right) = Played(Module(("sound", sound)), 2.0);

        Peak(left).ShouldBeGreaterThan(0.2f, "it should play with nothing wired");

        foreach (var sample in left.Concat(right)) sample.ShouldBeInRange(-1f, 1f);
    }

    /// <summary>Every knob and every setting at random, past both ends, and still never past full scale.</summary>
    [Fact]
    public void No_knob_or_setting_takes_it_past_full_scale()
    {
        var random = new Random(11);
        var inputs = Catalog.Require(Drum).Inputs;

        for (var trial = 0; trial < 80; trial++)
        {
            var node = Module(("sound", Pick(Sounds)), ("rhythm", Pick(Rhythms)));

            for (var port = 1; port < inputs.Count; port++)
            {
                var spec = inputs[port];
                var span = spec.Max - spec.Min;
                node.InputValues[port] = spec.Min - span + (float)random.NextDouble() * span * 3f;
            }

            var (left, right) = Played(node, 0.25);

            foreach (var sample in left.Concat(right))
            {
                float.IsFinite(sample).ShouldBeTrue($"trial {trial}");
                sample.ShouldBeInRange(-1f, 1f, $"trial {trial}");
            }
        }

        string Pick(string[] from) => from[random.Next(from.Length)];
    }

    /// <summary>At 120 bpm a beat is half a second and a sixteenth an eighth of one.</summary>
    [Theory]
    [InlineData("kick", new[] { 0.0, 0.5, 1.0, 1.5 })]
    [InlineData("snare", new[] { 0.5, 1.5 })]
    [InlineData("open hat", new[] { 0.25, 0.75, 1.25, 1.75 })]
    public void On_auto_each_sound_plays_the_rhythm_that_suits_it(string sound, double[] hits)
    {
        var env = Played(Module(("sound", sound)), 2.0, Env).Left;

        Near(Onsets(env), hits);
    }

    /// <summary>A psy kick is silent by the next sixteenth whatever the tempo, so a bass on it is heard alone.</summary>
    [Theory]
    [InlineData(120f)]
    [InlineData(145f)]
    [InlineData(175f)]
    public void A_psy_kick_is_gone_before_the_next_sixteenth(float bpm)
    {
        foreach (var decay in (float[])[0f, 0.5f, 1f])
        {
            var node = Module(("sound", "psy kick"));
            node.InputValues[Bpm] = bpm;
            node.InputValues[Decay] = decay;

            var sixteenth = 15.0 / bpm;
            var (left, _) = Played(node, sixteenth * 2);

            Peak(left[..(int)(sixteenth * Rate / 2)]).ShouldBeGreaterThan(0.5f, $"{bpm} bpm, decay {decay}");
            Peak(left[(int)(sixteenth * Rate)..]).ShouldBeLessThan(1e-4f, $"{bpm} bpm, decay {decay}");
        }
    }

    [Fact]
    public void Its_tempo_is_in_beats_a_minute()
    {
        var node = Module(("sound", "kick"));
        node.InputValues[Bpm] = 90f;

        Near(Onsets(Played(node, 2.1, Env).Left), [0.0, 2.0 / 3.0, 4.0 / 3.0, 2.0]);
    }

    [Fact]
    public void Swing_plays_every_second_sixteenth_late()
    {
        var node = Module(("sound", "rim"), ("rhythm", "sixteenths"));
        node.InputValues[Swing] = 1f;

        var struck = Onsets(Played(node, 0.5, Env).Left);

        // A pair of sixteenths is a quarter of a second; full swing plays the second three quarters in.
        Near(struck, [0.0, 0.1875, 0.25, 0.4375]);
    }

    [Fact]
    public void On_trigger_it_is_silent_until_struck_and_then_plays_each_rise()
    {
        var b = new PatchBuilder(Catalog);
        var pulse = b.Add(NodeCatalog.PulseTypeId, (1, 2f), (3, 0.5f), (4, 0.5f), (5, 0.5f));
        var drum = Configure(b.Add(Drum), ("sound", "kick"), ("rhythm", "trigger"));
        var output = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));

        // High for the second half of each half second.
        b.Wire(pulse, 0, drum, Trigger).Wire(drum, Env, output, NodeCatalog.OutputLeftPort);

        var env = Played(b.Build(), 1.2).Left;

        env.AsSpan(0, Rate / 5).ToArray().Max().ShouldBe(0f);
        Near(Onsets(env), [0.25, 0.75]);
    }

    [Fact]
    public void A_longer_decay_rings_longer()
    {
        var shortNode = Module(("sound", "kick"));
        shortNode.InputValues[Decay] = 0f;
        var longNode = Module(("sound", "kick"));
        longNode.InputValues[Decay] = 1f;

        var shortEnv = Played(shortNode, 0.45, Env).Left;
        var longEnv = Played(longNode, 0.45, Env).Left;

        shortEnv[Rate / 5].ShouldBeLessThan(0.1f);
        longEnv[Rate / 5].ShouldBeGreaterThan(0.6f);
    }

    [Fact]
    public void Panned_hard_left_the_right_is_silent()
    {
        var node = Module(("sound", "snare"));
        node.InputValues[Pan] = -1f;

        var (left, right) = Played(node, 1.0);

        Peak(left).ShouldBeGreaterThan(0.2f);
        Peak(right).ShouldBe(0f);
    }

    /// <summary>On a rhythm nothing is remembered, so the screen flashes with the hits the speakers play.</summary>
    [Fact]
    public void The_screen_sees_the_hits_the_speakers_hear()
    {
        var node = Module(("sound", "snare"));
        var heard = Played(node, 1.0, Env).Left;

        var program = Wired(node, Env, NodeCatalog.OutputColorPort).CompileForVideo(Catalog).Program;
        var registers = program.AllocateRegisters();

        foreach (var seconds in (double[])[0.1, 0.52, 0.6, 0.9])
        {
            program.Evaluate(0.2, -0.3, seconds, registers, default);
            registers[program.OutputBase].ShouldBe(heard[(int)(seconds * Rate)], 1e-3, $"at {seconds} s");
        }
    }

    [Theory]
    [MemberData(nameof(EverySound))]
    public void Every_sound_lowers_to_the_screen(string sound)
    {
        foreach (var rhythm in (string[])["auto", "trigger"])
        {
            var program = Wired(Module(("sound", sound), ("rhythm", rhythm)), Left, NodeCatalog.OutputColorPort)
                .CompileForVideo(Catalog).Program;

            foreach (var dialect in Enum.GetValues<GlslDialect>())
                GlslEmitter.Emit(program, dialect).PatchFragment.ShouldNotBeNullOrEmpty();
        }
    }

    private static NodeInstance Module(params (string Key, string Value)[] settings) =>
        Configure(NodeInstance.Create(Catalog.Require(Drum), 0, 0), settings);

    private static NodeInstance Configure(NodeInstance node, params (string Key, string Value)[] settings)
    {
        var state = new System.Text.Json.Nodes.JsonObject();
        foreach (var (key, value) in settings) state[key] = value;

        node.SetState("drum", state);

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

    /// <summary>The seconds at which an envelope jumps up: each hit.</summary>
    private static double[] Onsets(float[] env)
    {
        var onsets = new List<double>();
        var previous = 0f;

        for (var i = 0; i < env.Length; i++)
        {
            if (env[i] - previous > 0.3f) onsets.Add(i / (double)Rate);
            previous = env[i];
        }

        return [.. onsets];
    }

    private static float Peak(float[] sound) => sound.Max(Math.Abs);

    private static void Near(double[] actual, double[] expected)
    {
        actual.Length.ShouldBe(expected.Length, string.Join(", ", actual));
        for (var i = 0; i < expected.Length; i++) actual[i].ShouldBe(expected[i], 0.002);
    }
}
