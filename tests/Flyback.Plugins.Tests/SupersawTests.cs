using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// The Supersaw plugin, loaded off disk and evaluated register by register.
/// Nothing here goes through a renderer, so what is measured is the arithmetic
/// the module emitted rather than anything the audio path did to it afterwards.
/// </summary>
public class SupersawTests
{
    private const string Supersaw = "flyback.supersaw.osc";

    private static readonly ModuleCatalog Catalog = PluginHost.Load().Modules;

    // Supersaw ports, in order.
    private const int Freq = 1;
    private const int Detune = 2;
    private const int Mix = 3;

    [Fact]
    public void The_plugin_loads_and_offers_the_module()
    {
        Catalog.Get(Supersaw).ShouldNotBeNull().Name.ShouldBe("Supersaw");
        Catalog.ProviderOf(Supersaw)!.Id.ShouldBe("flyback.supersaw");
    }

    /// <summary>It should turn up beside the oscillators it belongs with.</summary>
    [Fact]
    public void It_joins_the_oscillator_category()
    {
        Catalog.Get(Supersaw)!.Category.ShouldBe("Oscillator");
        Catalog.Categories.Count(c => c == "Oscillator").ShouldBe(1);
    }

    [Fact]
    public void It_has_two_outputs_for_the_two_channels()
    {
        Catalog.Get(Supersaw)!.Outputs.Select(p => p.Name).ShouldBe(["out", "wide"]);
    }

    /// <summary>
    /// With the outer voices faded out there is one voice left, and it is a
    /// plain saw at the same pitch. A module that does not degenerate to the
    /// thing it is built from has a phase error hiding in it somewhere.
    /// </summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(0.5f)]
    [InlineData(1f)]
    public void At_no_mix_it_is_exactly_one_saw(float detune)
    {
        for (var step = 0; step < 64; step++)
        {
            var t = step * 0.05f;

            var super = Evaluate(Supersaw, t, (Freq, 2f), (Detune, detune), (Mix, 0f));
            var saw = Evaluate("osc.saw", t, (Freq, 2f));

            super[0].ShouldBe(saw[0], 1e-5f);
            super[1].ShouldBe(saw[0], 1e-5f);
        }
    }

    /// <summary>
    /// The claim the mixing maths makes: turning the outer voices up makes it
    /// wider, never louder. Seven saws summed without this would reach seven.
    /// </summary>
    [Fact]
    public void It_never_leaves_minus_one_to_one()
    {
        foreach (var detune in new[] { 0f, 0.25f, 0.5f, 1f })
            foreach (var mix in new[] { 0f, 0.3f, 0.75f, 1f })
                for (var step = 0; step < 400; step++)
                {
                    var value = Evaluate(Supersaw, step * 0.017f, (Freq, 3f), (Detune, detune), (Mix, mix));

                    value[0].ShouldBeInRange(-1.0001f, 1.0001f);
                    value[1].ShouldBeInRange(-1.0001f, 1.0001f);
                }
    }

    /// <summary>Out of range on a patched input must not divide by a cancelled sum.</summary>
    [Fact]
    public void A_mix_outside_its_range_is_held_at_the_edge()
    {
        var below = Evaluate(Supersaw, 0.3f, (Freq, 2f), (Mix, -4f));
        var zero = Evaluate(Supersaw, 0.3f, (Freq, 2f), (Mix, 0f));
        var above = Evaluate(Supersaw, 0.3f, (Freq, 2f), (Mix, 9f));
        var one = Evaluate(Supersaw, 0.3f, (Freq, 2f), (Mix, 1f));

        below[0].ShouldBe(zero[0], 1e-5f);
        above[0].ShouldBe(one[0], 1e-5f);
    }

    [Fact]
    public void Detuning_actually_changes_the_sound()
    {
        var plain = Samples(0f);
        var spread = Samples(0.6f);

        plain.Zip(spread).ShouldContain(pair => Math.Abs(pair.First - pair.Second) > 0.05f);

        float[] Samples(float detune) =>
        [
            .. Enumerable.Range(0, 200)
                .Select(s => Evaluate(Supersaw, s * 0.013f, (Freq, 4f), (Detune, detune), (Mix, 1f))[0]),
        ];
    }

    /// <summary>
    /// The two outputs weight the same voices differently, so once the voices
    /// have drifted apart the channels carry different signals. Identical
    /// channels would be stereo in name only.
    /// </summary>
    [Fact]
    public void The_two_outputs_differ_once_the_voices_are_spread()
    {
        var different = Enumerable.Range(0, 200)
            .Select(s => Evaluate(Supersaw, s * 0.013f, (Freq, 4f), (Detune, 0.7f), (Mix, 1f)))
            .Any(v => Math.Abs(v[0] - v[1]) > 0.05f);

        different.ShouldBeTrue();
    }

    [Fact]
    public void The_plugin_offers_a_preset_after_the_engines_own()
    {
        var presets = PluginHost.Load().Presets;

        presets.Select(p => p.Name).ShouldContain("Supersaw");
        presets.Take(Presets.All.Count).Select(p => p.Name)
            .ShouldBe(Presets.All.Select(p => p.Name));
    }

    /// <summary>
    /// A preset is a lambda until something picks it, so registering one proves
    /// nothing. This builds it and puts it through both sinks, which is where a
    /// wrong port index or a module the plugin forgot to add would surface.
    /// </summary>
    [Fact]
    public void The_preset_builds_and_compiles_for_both_sinks()
    {
        var patch = PluginHost.Load().Presets.Single(p => p.Name == "Supersaw").Build(Catalog);

        patch.Nodes.Select(n => n.TypeId).ShouldContain(Supersaw);

        var video = patch.CompileForVideo(Catalog);
        var audio = patch.CompileForAudio(Catalog);

        video.Issues.ShouldBeEmpty();
        audio.Issues.ShouldBeEmpty();
        video.Program.Ops.ShouldNotBeEmpty();
        audio.Program.Ops.ShouldNotBeEmpty();
    }

    /// <summary>
    /// The mistake the preset exists to prevent: pitch has to come from a
    /// Frequency module, because 'freq' counts cycles per unit of 'in' and its
    /// knob would leave the oscillator down at one hertz, clicking.
    /// </summary>
    [Fact]
    public void The_preset_takes_its_pitch_from_a_frequency_module()
    {
        var patch = PluginHost.Load().Presets.Single(p => p.Name == "Supersaw").Build(Catalog);

        var voice = patch.Nodes.First(n =>
            n.TypeId == Supersaw && patch.IncomingTo(n.Id, Freq) is not null);

        var source = patch.Find(patch.IncomingTo(voice.Id, Freq)!.SourceNode);

        source.ShouldNotBeNull().TypeId.ShouldBe("audio.frequency");
    }

    /// <summary>Both outputs used, or the stereo spread is there for nothing.</summary>
    [Fact]
    public void The_preset_wires_both_outputs_to_the_speakers()
    {
        var patch = PluginHost.Load().Presets.Single(p => p.Name == "Supersaw").Build(Catalog);
        var sink = patch.Output;

        var left = patch.IncomingTo(sink.Id, NodeCatalog.OutputLeftPort).ShouldNotBeNull();
        var right = patch.IncomingTo(sink.Id, NodeCatalog.OutputRightPort).ShouldNotBeNull();

        left.SourceNode.ShouldBe(right.SourceNode);
        left.SourcePort.ShouldBe(0);
        right.SourcePort.ShouldBe(1);
    }

    /// <summary>
    /// The preset must make a sound in the audio band, which is the whole reason
    /// it exists — a patch that clicks once a second would pass every other test
    /// here. Counting sample-to-sample steps distinguishes a tone from clicks.
    /// </summary>
    [Fact]
    public void The_preset_actually_makes_a_tone()
    {
        var patch = PluginHost.Load().Presets.Single(p => p.Name == "Supersaw").Build(Catalog);
        var program = patch.CompileForAudio(Catalog).Program;
        var registers = program.AllocateRegisters();

        var previous = 0f;
        var steps = 0;

        for (var s = 0; s < AudioRenderer.DefaultSampleRate; s++)
        {
            program.Evaluate(0f, 0f, s / (double)AudioRenderer.DefaultSampleRate, registers, default);

            var value = (float)registers[program.OutputBase];
            if (s > 0 && MathF.Abs(value - previous) > 0.1f) steps++;
            previous = value;
        }

        // A 110 Hz supersaw resets seven times a cycle; one hertz would be single figures.
        steps.ShouldBeGreaterThan(200);
    }

    /// <summary>
    /// The sound itself. Detuned voices drift in and out of step, so the
    /// envelope swells and dips at a few hertz — that slow beating is what a
    /// supersaw *is*, and seven saws at one pitch would not do it.
    /// </summary>
    [Fact]
    public void Detuned_voices_beat_against_each_other()
    {
        // Measured: about 1.04 with the voices together, and over 3 apart. The
        // flat case is not exactly 1 because a 5 ms window is not a whole number
        // of cycles at 220 Hz, so it clips a slightly different part each time.
        PeakSwing(detune: 0f).ShouldBeLessThan(1.2f);
        PeakSwing(detune: 0.6f).ShouldBeGreaterThan(2f);
    }

    /// <summary>
    /// Loudest 5 ms window against the quietest, over half a second. A window is
    /// longer than one cycle at 220 Hz, so a steady tone measures the same in
    /// every window and only a moving envelope spreads them apart.
    /// </summary>
    private static float PeakSwing(float detune)
    {
        const int windows = 100;
        const int samples = 240;

        var signal = Signal(Supersaw, (Freq, 220f), (Detune, detune), (Mix, 1f));
        var peaks = new float[windows];

        for (var w = 0; w < windows; w++)
        {
            var peak = 0f;

            for (var s = 0; s < samples; s++)
                peak = MathF.Max(peak, MathF.Abs(signal((w * samples + s) / (float)AudioRenderer.DefaultSampleRate)[0]));

            peaks[w] = peak;
        }

        return peaks.Max() / peaks.Min();
    }

    private static float[] Evaluate(string typeId, float time, params (int Port, float Value)[] knobs) =>
        Signal(typeId, knobs)(time);

    /// <summary>
    /// Compiles a one-oscillator patch into the audio sink once, and hands back
    /// something that reads the two output registers straight out of the machine
    /// at a given moment.
    /// </summary>
    private static Func<float, float[]> Signal(string typeId, params (int Port, float Value)[] knobs)
    {
        var patch = new Patch();

        var clock = Add(patch, "time", (0, 1f));
        var osc = Add(patch, typeId, knobs);
        var sink = Add(patch, NodeCatalog.OutputTypeId, (NodeCatalog.OutputGainPort, 1f));

        patch.Connect(clock.Id, 0, osc.Id, 0);
        patch.Connect(osc.Id, 0, sink.Id, NodeCatalog.OutputLeftPort);

        // 'wide' where the module has one, otherwise the same output twice —
        // which is what the audio sink's normalled right channel would do anyway.
        var right = Catalog.Require(typeId).Outputs.Count > 1 ? 1 : 0;
        patch.Connect(osc.Id, right, sink.Id, NodeCatalog.OutputRightPort);

        var program = patch.CompileForAudio(Catalog).Program;
        var registers = program.AllocateRegisters();

        return time =>
        {
            program.Evaluate(0f, 0f, time, registers, default);
            return [(float)registers[program.OutputBase], (float)registers[program.OutputBase + 1]];
        };
    }

    private static NodeInstance Add(Patch patch, string typeId, params (int Port, float Value)[] knobs)
    {
        var node = NodeInstance.Create(Catalog.Require(typeId), 0, 0);

        foreach (var (port, value) in knobs) node.InputValues[port] = value;

        patch.Nodes.Add(node);
        return node;
    }
}
