using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// The Played preset: four voices, each a key plucking a string, into a little reverb.
/// </summary>
public class PlayedPresetTests
{
    private const int Rate = GlobalConstants.SampleRate;
    private const int Voices = 4;

    private static readonly PluginCatalog Loaded = ShippedPlugins.Loaded;

    private static Patch Patch() => Loaded.Presets.Single(p => p.Name == "Played").Build(Loaded.Modules);

    private static string Key(int voice, string signal) => MidiSignal.Key(MidiSources.Keyboard, voice, signal);

    /// <summary>One note: which voice plays it, what pitch, and when it is let go (never, at -1).</summary>
    private readonly record struct Held(int Voice, float Pitch, int ReleaseAt = -1);

    [Fact]
    public void It_is_a_sound_idea_from_the_effects_plugin()
    {
        Loaded.Presets.Single(p => p.Name == "Played").Kind.ShouldBe(PresetKind.Idea);
        Presets.All.ShouldNotContain(p => p.Name == "Played");
    }

    [Fact]
    public void It_is_four_voices_of_key_and_string_into_one_room()
    {
        var patch = Patch();
        var types = patch.Nodes.Select(n => n.TypeId).ToList();

        types.Count(t => t == NodeCatalog.MidiTypeId).ShouldBe(Voices);
        types.Count(t => t == NodeCatalog.StringTypeId).ShouldBe(Voices);
        types.Count(t => t == "audio.note").ShouldBe(Voices);
        types.Count(t => t == NodeCatalog.ReverbTypeId).ShouldBe(1);
        types.Count(t => t == "math.mixer").ShouldBe(1);

        patch.Nodes
            .Where(n => n.TypeId == NodeCatalog.MidiTypeId)
            .Select(n => (int)n.StateOf(MidiExtra.StateKey)![MidiExtra.IndexField]!.GetValue<float>())
            .Order()
            .ShouldBe([1, 2, 3, 4]);
    }

    [Fact]
    public void It_draws_nothing()
    {
        var patch = Patch();
        var sink = patch.FirstOf(NodeCatalog.OutputTypeId).ShouldNotBeNull();

        patch.Connections.ShouldNotContain(w => w.TargetNode == sink.Id && w.TargetPort == NodeCatalog.OutputColorPort);
    }

    [Fact]
    public void The_sound_compiles_clean_and_hears_every_voice()
    {
        var compiled = Patch().CompileForAudio(Loaded.Modules);

        compiled.Issues.ShouldBeEmpty(string.Join("; ", compiled.Issues.Select(i => i.Message)));

        for (var voice = 1; voice <= Voices; voice++)
        {
            compiled.Program.LiveInputs.ShouldContain(Key(voice, MidiSignal.Pitch));
            compiled.Program.LiveInputs.ShouldContain(Key(voice, MidiSignal.Gate));
            compiled.Program.LiveInputs.ShouldContain(Key(voice, MidiSignal.Strikes));
        }
    }

    /// <summary>Velocity is left alone: a typist strikes every key the same.</summary>
    [Fact]
    public void Nothing_is_wired_to_velocity()
    {
        var patch = Patch();

        foreach (var keys in patch.Nodes.Where(n => n.TypeId == NodeCatalog.MidiTypeId))
        {
            // 0 pitch, 1 gate, 2 velocity, 3 trigger.
            patch.Connections
                .Where(wire => wire.SourceNode == keys.Id)
                .Select(wire => wire.SourcePort)
                .Distinct()
                .Order()
                .ShouldBe([0, 1, 3]);
        }
    }

    [Fact]
    public void Nothing_held_is_silence()
    {
        Loudest(Play(Rate / 2).Left).ShouldBe(0f);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Each_voice_plays_its_note_at_its_pitch(int voice)
    {
        var (left, right) = Play(Rate / 2, new Held(voice, 57f));

        Loudest(left).ShouldBeGreaterThan(0.05f);
        Loudest(right).ShouldBeGreaterThan(0.05f);
        Pitch(left[(Rate / 10)..]).ShouldBe(220f, 220f * 0.02f);
    }

    [Theory]
    [InlineData(69f, 440f)]
    [InlineData(57f, 220f)]
    public void The_note_played_is_the_pitch_heard(float note, float hz)
    {
        Pitch(Play(Rate / 2, new Held(1, note)).Left[(Rate / 10)..]).ShouldBe(hz, hz * 0.02f);
    }

    /// <summary>A chord is its notes summed: four voices at once, none of them taking another's place.</summary>
    [Fact]
    public void Four_notes_sound_at_once()
    {
        float[] pitches = [57f, 61f, 64f, 69f];

        var chord = Play(Rate / 2, [.. pitches.Select((p, v) => new Held(v + 1, p))]).Left;
        var alone = pitches.Select((p, v) => Play(Rate / 2, new Held(v + 1, p)).Left).ToList();

        // Everything after the strings is linear, so the chord is exactly the four notes played on their own.
        for (var i = 0; i < chord.Length; i += 97)
            chord[i].ShouldBe(alone.Sum(note => note[i]), 1e-4f);
    }

    [Fact]
    public void The_room_is_stereo()
    {
        var (left, right) = Play(Rate / 2, new Held(1, 69f));

        left.Zip(right, (l, r) => MathF.Abs(l - r)).Max().ShouldBeGreaterThan(0.01f);
    }

    /// <summary>Letting go damps that string, leaving only the room's short tail.</summary>
    [Fact]
    public void Letting_go_damps_the_string()
    {
        var held = Play(Rate, new Held(1, 69f)).Left;
        var released = Play(Rate, new Held(1, 69f, ReleaseAt: Rate / 4)).Left;

        // Late enough that the room has let go of the pluck too.
        var window = (Rate * 3 / 4, Rate);
        var ringing = Loudest(held[window.Item1..window.Item2]);

        ringing.ShouldBeGreaterThan(0.002f);
        Loudest(released[window.Item1..window.Item2]).ShouldBeLessThan(ringing * 0.25f);
    }

    [Fact]
    public void Letting_go_of_one_note_leaves_the_others_ringing()
    {
        var oneLetGo = Play(Rate, new Held(1, 69f, ReleaseAt: Rate / 4), new Held(2, 64f)).Left;
        var otherAlone = Play(Rate, new Held(2, 64f)).Left;
        var firstLetGo = Play(Rate, new Held(1, 69f, ReleaseAt: Rate / 4)).Left;

        // Linear after the strings: the pair is the held note plus the damped one.
        for (var i = 0; i < oneLetGo.Length; i += 97)
            oneLetGo[i].ShouldBe(otherAlone[i] + firstLetGo[i], 1e-4f);

        Loudest(otherAlone[(Rate * 3 / 4)..]).ShouldBeGreaterThan(Loudest(firstLetGo[(Rate * 3 / 4)..]) * 4f);
    }

    // --- harness -----------------------------------------------------------------

    private static (float[] Left, float[] Right) Play(int length, params Held[] notes)
    {
        var program = Patch().CompileForAudio(Loaded.Modules).Program;
        var registers = program.AllocateRegisters();
        var state = new DelayState(program.DelayLengths, Rate, program.PhaseCount, program.UnitCount);
        var live = new LiveValues(program.LiveInputs);

        foreach (var note in notes)
        {
            live.Set(Key(note.Voice, MidiSignal.Pitch), note.Pitch);
            live.Set(Key(note.Voice, MidiSignal.Gate), 1f);
        }

        var left = new float[length];
        var right = new float[length];

        for (var i = 0; i < length; i++)
        {
            foreach (var note in notes)
            {
                // Struck on the second sample: the trigger is a change in the strike count, and
                // the first evaluation has no memory to compare against.
                if (i == 1) live.Set(Key(note.Voice, MidiSignal.Strikes), 1f);
                if (i == note.ReleaseAt) live.Set(Key(note.Voice, MidiSignal.Gate), 0f);
            }

            program.Evaluate(0, 0, i / (double)Rate, registers, default, state, live: live);
            left[i] = (float)registers[program.OutputBase];
            right[i] = (float)registers[program.OutputBase + 1];
        }

        return (left, right);
    }

    private static float Loudest(float[] samples) => samples.Max(MathF.Abs);

    /// <summary>The frequency whose period best matches the signal to itself.</summary>
    private static float Pitch(float[] signal)
    {
        var scores = new double[Rate / 40 + 2];
        var best = 0;

        for (var lag = Rate / 2000; lag < scores.Length - 1; lag++)
        {
            double sum = 0;
            for (var i = 0; i + lag < signal.Length; i++) sum += signal[i] * signal[i + lag];
            scores[lag] = sum / (signal.Length - lag);

            if (scores[lag] > scores[best]) best = lag;
        }

        // Correlation also peaks at every multiple of the period; take the shortest that nearly matches.
        for (var lag = Rate / 2000; lag < best; lag++)
            if (scores[lag] > 0.9 * scores[best] && scores[lag] >= scores[lag - 1] && scores[lag] >= scores[lag + 1])
            {
                best = lag;
                break;
            }

        var (a, b, c) = (scores[best - 1], scores[best], scores[best + 1]);
        return (float)(Rate / (best + 0.5 * (a - c) / (a - 2 * b + c)));
    }
}
