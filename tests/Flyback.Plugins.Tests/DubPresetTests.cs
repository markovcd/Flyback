using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// The Dub preset: a backing that runs on its own, four played voices over it, and a
/// panel of knobs. Building, compiling and layout are covered for every preset in
/// <see cref="ShippedPresetTests"/>; this checks that it can be played and ridden.
/// </summary>
public class DubPresetTests
{
    private const int Rate = GlobalConstants.SampleRate;
    private const int Voices = 4;

    private static readonly PluginCatalog Loaded = PluginHost.Load();

    private static Patch Patch() => Loaded.Presets.Single(p => p.Name == "Dub").Build(Loaded.Modules);

    private static string Key(int voice, string signal) => MidiSignal.Key(MidiSources.Keyboard, voice, signal);

    [Fact]
    public void It_is_a_showcase()
    {
        Loaded.Presets.Single(p => p.Name == "Dub").Kind.ShouldBe(PresetKind.Showcase);
    }

    [Fact]
    public void Four_keys_are_four_voices()
    {
        Patch().Nodes
            .Where(n => n.TypeId == NodeCatalog.MidiTypeId)
            .Select(n => (int)n.StateOf(MidiExtra.StateKey)![MidiExtra.IndexField]!.GetValue<float>())
            .Order()
            .ShouldBe([1, 2, 3, 4]);
    }

    [Fact]
    public void The_panel_has_a_row_of_eight()
    {
        Patch().Controls.ShouldNotBeNull().Select(c => c.Name).ShouldBe(
            ["Cutoff", "Resonance", "Pluck", "Decay", "Echo", "Space", "Drums", "Bass"]);
    }

    /// <summary>Nothing is bound: which controller a knob follows is the player's to say.</summary>
    [Fact]
    public void No_knob_arrives_bound_to_a_controller()
    {
        Patch().Controls.ShouldNotBeNull().ShouldAllBe(c => c.Midi == null);
    }

    [Fact]
    public void Every_knob_is_followed_and_every_link_names_a_knob()
    {
        var patch = Patch();

        foreach (var control in patch.Controls.ShouldNotBeNull())
            ControlMap.Following(patch, control.Id).ShouldNotBeEmpty($"nothing follows '{control.Name}'");

        foreach (var node in patch.Nodes)
        foreach (var (_, link) in ControlMap.All(node))
            patch.Control(link.Control).ShouldNotBeNull();
    }

    /// <summary>
    /// A link is not held to its socket's range when it is compiled, so a preset has
    /// to keep to it: the inspector draws the socket's own range under the knob's.
    /// </summary>
    [Fact]
    public void Every_link_keeps_to_its_sockets_range()
    {
        var patch = Patch();

        foreach (var node in patch.Nodes)
        foreach (var (port, link) in ControlMap.All(node))
        {
            var spec = Loaded.Modules.Require(node.TypeId).Inputs[port];

            foreach (var end in new[] { link.Min, link.Max })
                end.ShouldBeInRange(spec.Min, spec.Max, $"{node.TypeId} '{spec.Name}'");
        }
    }

    /// <summary>A linked socket rests where its knob does, so baking the knobs in changes nothing.</summary>
    [Fact]
    public void Every_linked_socket_rests_where_its_knob_does()
    {
        var patch = Patch();

        foreach (var node in patch.Nodes)
        foreach (var (port, link) in ControlMap.All(node))
            node.InputValues[port].ShouldBe(link.At(patch.Control(link.Control)!.Value), 1e-6f);
    }

    [Fact]
    public void Both_sinks_read_the_keys_and_the_knobs()
    {
        var patch = Patch();

        foreach (var compiled in new[]
                 {
                     patch.CompileForAudio(Loaded.Modules, played: true),
                     patch.CompileForVideo(Loaded.Modules, played: true),
                 })
        {
            compiled.Issues.ShouldBeEmpty(string.Join("; ", compiled.Issues.Select(i => i.Message)));

            for (var voice = 1; voice <= Voices; voice++)
                compiled.Program.LiveInputs.ShouldContain(Key(voice, MidiSignal.Pitch));

            compiled.Program.LiveInputs.ShouldContain(patch.Controls!.Single(c => c.Name == "Echo").Key);
        }
    }

    /// <summary>The picture is told how loud a voice is: an envelope has no memory on the screen.</summary>
    [Fact]
    public void Each_voice_has_a_meter_for_the_picture()
    {
        Patch().Nodes.Count(n => n.TypeId == NodeCatalog.MeterTypeId).ShouldBe(Voices);
    }

    [Fact]
    public void A_chord_is_louder_than_the_backing_alone()
    {
        var backing = Play(Rate);
        var played = Play(Rate, [57f, 60f, 64f, 67f]);

        Loudness(played).ShouldBeGreaterThan(Loudness(backing) * 1.1f);
    }

    [Fact]
    public void The_drums_and_the_bass_come_down_to_the_dust()
    {
        var up = Play(Rate / 2);
        var down = Play(Rate / 2, knobs: [("Drums", 0f), ("Bass", 0f)]);

        Loudness(down).ShouldBeLessThan(Loudness(up) * 0.2f);
    }

    [Fact]
    public void Opening_the_filter_brightens_a_chord()
    {
        (string, float)[] quiet = [("Drums", 0f), ("Bass", 0f)];

        var shut = Play(Rate / 2, [57f, 60f, 64f, 67f], [.. quiet, ("Cutoff", 0f), ("Pluck", 0f)]);
        var open = Play(Rate / 2, [57f, 60f, 64f, 67f], [.. quiet, ("Cutoff", 1f), ("Pluck", 0f)]);

        // The dust is the same in both and is most of what is bright in either, so
        // twice over is the chord's top arriving and not a rounding.
        Brightness(open).ShouldBeGreaterThan(Brightness(shut) * 2f);
    }

    [Fact]
    public void Nothing_reaches_the_rails_with_every_knob_at_rest()
    {
        Play(Rate * 2, [57f, 60f, 64f, 67f]).Max(MathF.Abs).ShouldBeLessThan(0.85f);
    }

    // --- harness -----------------------------------------------------------------

    /// <summary>The left channel, with <paramref name="chord"/> struck a tenth of a second in and held.</summary>
    private static float[] Play(int length, float[]? chord = null, (string Name, float Value)[]? knobs = null)
    {
        var patch = Patch();
        var program = patch.CompileForAudio(Loaded.Modules, played: true).Program;
        var registers = program.AllocateRegisters();
        var state = new DelayState(program.DelayLengths, Rate, program.PhaseCount, program.UnitCount);
        var live = new LiveValues(program.LiveInputs);

        patch.Seed(live);

        foreach (var (name, value) in knobs ?? [])
            live.Set(patch.Controls!.Single(c => c.Name == name).Key, value);

        var left = new float[length];

        for (var i = 0; i < length; i++)
        {
            if (i == Rate / 10)
                for (var voice = 1; voice <= (chord?.Length ?? 0); voice++)
                {
                    live.Set(Key(voice, MidiSignal.Pitch), chord![voice - 1]);
                    live.Set(Key(voice, MidiSignal.Velocity), 0.8f);
                    live.Set(Key(voice, MidiSignal.Gate), 1f);
                    live.Set(Key(voice, MidiSignal.Strikes), 1f);
                }

            program.Evaluate(0, 0, i / (double)Rate, registers, default, state, live: live);
            left[i] = (float)registers[program.OutputBase];
        }

        return left;
    }

    private static float Loudness(float[] samples) =>
        MathF.Sqrt(samples.Sum(x => x * x) / samples.Length);

    /// <summary>How loud the signal's sample-to-sample change is, which is what a lowpass takes away.</summary>
    private static float Brightness(float[] samples) =>
        Loudness([.. samples.Skip(1).Zip(samples, (now, before) => now - before)]);
}
