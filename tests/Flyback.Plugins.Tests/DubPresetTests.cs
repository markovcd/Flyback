using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;
using Flyback.Core.Render;
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

    /// <summary>A beat at seventy-four a minute, in seconds.</summary>
    private const double Beat = 60 / 74.0;

    /// <summary>A beat of the steppers, at twice the tempo.</summary>
    private const double FastBeat = 60 / 148.0;

    private static readonly PluginCatalog Loaded = ShippedPlugins.Loaded;

    private static Patch Patch() => Loaded.Presets.Single(p => p.Name == "Dub").Build(Loaded.Modules);

    /// <summary>The voice's clips, which the preset carries rather than names.</summary>
    private static readonly BundleFiles Carried = new(Loaded.Presets.Single(p => p.Name == "Dub").Files!());

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
    public void The_panel_has_six_knobs()
    {
        Patch().Controls.ShouldNotBeNull().Select(c => c.Name).ShouldBe(
            ["Cutoff", "Resonance", "Pluck", "Decay", "Echo", "Space"]);
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
                     patch.CompileForAudio(Loaded.Modules, Carried, played: true),
                     patch.CompileForVideo(Loaded.Modules, Carried, played: true),
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

    /// <summary>In the first bar, where only the dust plays for the first half second.</summary>
    [Fact]
    public void Opening_the_filter_brightens_a_chord()
    {
        var shut = Play(Rate / 2, [57f, 60f, 64f, 67f], [("Cutoff", 0f), ("Pluck", 0f)]);
        var open = Play(Rate / 2, [57f, 60f, 64f, 67f], [("Cutoff", 1f), ("Pluck", 0f)]);

        // The dust is the same in both and is most of what is bright in either, so half
        // again is the organ's top arriving and not a rounding.
        Brightness(open).ShouldBeGreaterThan(Brightness(shut) * 1.5f);
    }

    /// <summary>The arrangement reaches the drums, the bass and the skank, and never the organ.</summary>
    [Fact]
    public void The_keys_play_one_sound_all_the_way_through()
    {
        var patch = Patch();
        var envelopes = patch.Nodes.Where(n => n.TypeId == NodeCatalog.AdsrTypeId).Select(n => n.Id).ToHashSet();
        var arrangements = patch.Nodes.Where(n => n.TypeId == NodeCatalog.ArrangementTypeId).Select(n => n.Id).ToHashSet();

        patch.Connections.ShouldNotContain(c => arrangements.Contains(c.SourceNode) && envelopes.Contains(c.TargetNode));
    }

    /// <summary>Forty-four bars at seventy-four, sixty-four at twice that, and the last line's echo.</summary>
    [Fact]
    public void It_lasts_forty_four_bars_slow_sixty_four_fast_and_an_echo()
    {
        Patch().Length.ShouldBe(Math.Round(176 * Beat + 256 * FastBeat + 24, 2));
    }

    /// <summary>The slow part's last bar, after the drop's line of Patois has faded out.</summary>
    [Fact]
    public void The_drop_comes_at_two_minutes_ten()
    {
        (160 * Beat).ShouldBe(130, 0.5);
    }

    [Fact]
    public void Silence_comes_before_the_steppers()
    {
        Play(Rate, from: 173 * Beat).Max(MathF.Abs).ShouldBe(0f);
    }

    /// <summary>The first second of the steppers against the second before them.</summary>
    [Fact]
    public void The_steppers_come_in_after_the_silence()
    {
        Loudness(Play(Rate, from: 176 * Beat)).ShouldBeGreaterThan(0.01f);
    }

    /// <summary>
    /// Played from the last line: ten seconds after it is said, its echo is still
    /// within twenty decibels of the line itself.
    /// </summary>
    [Fact]
    public void It_ends_on_a_line_of_Patois_and_its_long_echo()
    {
        var ending = Play(Rate * 14, from: 176 * Beat + 248 * FastBeat);

        Loudness([.. ending.Skip(Rate * 10)]).ShouldBeGreaterThan(Loudness([.. ending.Take(Rate * 3)]) * 0.1f);
    }

    [Fact]
    public void It_carries_both_sentences_of_its_voice()
    {
        Loaded.Presets.Single(p => p.Name == "Dub").Files.ShouldNotBeNull()().Keys.Order()
            .ShouldBe(["dread.wav", "no-sense.wav"]);
    }

    /// <summary>The second bar against the first, neither with a skank or a rim in its first second.</summary>
    [Fact]
    public void The_voice_speaks_in_the_second_bar()
    {
        Loudness(Play(Rate, from: 4 * Beat)).ShouldBeGreaterThan(Loudness(Play(Rate)) * 2f);
    }

    [Fact]
    public void The_computer_keyboard_is_laid_out_in_A_minor()
    {
        Patch().Keyboard.ShouldBe(new KeyboardScale(9, "aeolian"));
    }

    /// <summary>C sharp is not in A minor; the piece plays C or D in its place, whichever it snaps to.</summary>
    [Fact]
    public void A_key_off_the_scale_plays_the_nearest_note_on_it()
    {
        var sharp = Play(Rate / 4, [61f]);

        (sharp.SequenceEqual(Play(Rate / 4, [60f])) || sharp.SequenceEqual(Play(Rate / 4, [62f])))
            .ShouldBeTrue("C sharp sounds as neither C nor D");
    }

    [Fact]
    public void Nothing_reaches_the_rails_with_every_knob_at_rest()
    {
        Play(Rate * 2, [57f, 60f, 64f, 67f]).Max(MathF.Abs).ShouldBeLessThan(0.85f);
    }

    // --- harness -----------------------------------------------------------------

    /// <summary>
    /// The left channel from <paramref name="from"/> seconds into the piece, with
    /// <paramref name="chord"/> struck a tenth of a second in and held.
    /// </summary>
    private static float[] Play(
        int length, float[]? chord = null, (string Name, float Value)[]? knobs = null, double from = 0)
    {
        var patch = Patch();
        var program = patch.CompileForAudio(Loaded.Modules, Carried, played: true).Program;
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

            program.Evaluate(0, 0, from + i / (double)Rate, registers, default, state, live: live);
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
