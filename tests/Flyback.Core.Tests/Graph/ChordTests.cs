using System.Text.Json.Nodes;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>Chord, which plays a chord picked by number, and Auto Chord, which builds one from a scale.</summary>
public class ChordTests
{
    private const int Rate = 48_000;

    [Fact]
    public void A_chord_is_a_pitch_module_with_four_frequencies()
    {
        var def = NodeCatalog.BuiltIn.Require(NodeCatalog.ChordTypeId);

        def.Name.ShouldBe("Chord");
        def.Category.ShouldBe(ModuleCategories.Pitch);
        def.Inputs.Select(p => p.Name).ShouldBe(["note", "chord"]);
        def.Outputs.Select(p => p.Name).ShouldBe(["hz1", "hz2", "hz3", "hz4"]);
    }

    [Theory]
    [InlineData(4f, "maj")]
    [InlineData(10f, "maj7")]
    [InlineData(0f, "5th")]
    [InlineData(99f, "7sus4")]
    public void The_chord_knob_says_which_chord_it_picks(float value, string shown) =>
        NodeCatalog.BuiltIn.Require(NodeCatalog.ChordTypeId).Inputs[1].Format(value).ShouldBe(shown);

    [Fact]
    public void Every_chord_plays_its_four_notes_above_the_root()
    {
        for (var index = 0; index < Chords.All.Count; index++)
        {
            var picked = index;

            Heard(b => b.Add(NodeCatalog.ChordTypeId, (0, 57f), (1, picked)), 0).ShouldBe(Notes(57, Chords.Voiced(index)), 1e-3, Chords.All[index].Name);
        }
    }

    [Fact]
    public void A_triad_adds_its_root_an_octave_up() =>
        Chords.Voiced(Chords.Major).ShouldBe([0, 4, 7, 12]);

    [Fact]
    public void A_two_note_chord_adds_both_notes_an_octave_up() =>
        Chords.Voiced(0).ShouldBe([0, 7, 12, 19]);

    [Theory]
    [InlineData(4.4, 4)]
    [InlineData(10.6, 11)]
    [InlineData(-3, 0)]
    [InlineData(99, 22)]
    public void A_patched_chord_is_rounded_and_held_to_the_list(double patched, int played)
    {
        Heard(b =>
        {
            var chord = b.Add(NodeCatalog.ChordTypeId, (0, 60f));
            b.Wire(b.Add("coord"), 0, chord, 1);
            return chord;
        }, patched).ShouldBe(Notes(60, Chords.Voiced(played)), 1e-3);
    }

    [Fact]
    public void An_auto_chord_offers_the_modes_and_starts_in_major()
    {
        var def = NodeCatalog.BuiltIn.Require(NodeCatalog.AutoChordTypeId);

        def.Inputs.Select(p => p.Name).ShouldBe(["tonic", "root", "note"]);
        def.Outputs.Select(p => p.Name).ShouldBe(["hz1", "hz2", "hz3", "hz4"]);

        var scale = def.Extra<SettingsExtra>()!.Fields.OfType<ExtraField.Choice>().Single();
        scale.Options.Count.ShouldBe(Chords.Scales.Count);
        scale.Fallback.ShouldBe("ionian");
    }

    [Fact]
    public void Every_scale_is_seven_notes_up_from_its_tonic()
    {
        foreach (var scale in Chords.Scales)
        {
            scale.Classes.Length.ShouldBe(7, scale.Name);
            scale.Classes[0].ShouldBe(0, scale.Name);
            scale.Classes.ShouldBe(scale.Classes.Order().Distinct().ToArray(), scale.Name);
            scale.Classes.ShouldAllBe(c => c < 12, scale.Name);
        }

        Chords.Scales.Select(s => s.Id).ShouldBeUnique();
        Chords.Scale("phrygian-dominant").Classes.ShouldBe([0, 1, 4, 5, 7, 8, 10]);
        Chords.Scale("altered").Classes.ShouldBe([0, 1, 3, 4, 6, 8, 10]);
        Chords.Scale("dorian").Classes.ShouldBe([0, 2, 3, 5, 7, 9, 10]);
    }

    [Theory]
    [InlineData("ionian", 60, 0, new[] { 60, 64, 67, 71 })]
    [InlineData("ionian", 60, 1, new[] { 62, 65, 69, 72 })]
    [InlineData("ionian", 60, 4, new[] { 67, 71, 74, 77 })]
    [InlineData("ionian", 60, -1, new[] { 59, 62, 65, 69 })]
    [InlineData("ionian", 60, 7, new[] { 72, 76, 79, 83 })]
    [InlineData("ionian", 60, -8, new[] { 47, 50, 53, 57 })]
    [InlineData("ionian", 48, 2, new[] { 52, 55, 59, 62 })]
    [InlineData("aeolian", 69, 2, new[] { 72, 76, 79, 83 })]
    [InlineData("harmonic-minor", 57, 4, new[] { 64, 68, 71, 74 })]
    [InlineData("harmonic-minor", 57, 0, new[] { 57, 60, 64, 68 })]
    public void An_auto_chord_is_the_seventh_chord_its_scale_builds_that_many_steps_from_the_tonic(
        string scale, int tonic, int root, int[] notes)
    {
        Heard(b =>
        {
            var chord = b.Add(NodeCatalog.AutoChordTypeId, (0, tonic), (1, root));
            Scaled(chord, scale);
            return chord;
        }, 0).ShouldBe(notes.Select(n => (double)Pitch.Frequency(n)).ToArray(), 1e-3);
    }

    [Fact]
    public void A_patched_root_plays_every_chord_of_every_scale()
    {
        foreach (var scale in Chords.Scales)
        {
            var program = Compiled(b =>
            {
                var chord = b.Add(NodeCatalog.AutoChordTypeId, (0, 62f));
                Scaled(chord, scale.Id);
                b.Wire(b.Add("coord"), 0, chord, 1);
                return chord;
            });

            for (var root = -15; root <= 15; root++)
                Heard(program, root).ShouldBe(Notes(62, Chords.Diatonic(scale, root)), 1e-3, $"{scale.Name} on {root}");
        }
    }

    [Theory]
    [InlineData("ionian", 60, 62, 0, new[] { 62, 65, 69, 72 })]
    [InlineData("ionian", 60, 61, 0, new[] { 62, 65, 69, 72 })]
    [InlineData("ionian", 60, 59, 0, new[] { 59, 62, 65, 69 })]
    [InlineData("ionian", 60, 74, 0, new[] { 74, 77, 81, 84 })]
    [InlineData("ionian", 60, 48, 0, new[] { 48, 52, 55, 59 })]
    [InlineData("ionian", 60, 62, 1, new[] { 64, 67, 71, 74 })]
    [InlineData("ionian", 60, 62, -2, new[] { 59, 62, 65, 69 })]
    [InlineData("harmonic-minor", 57, 64, 0, new[] { 64, 68, 71, 74 })]
    [InlineData("harmonic-minor", 57, 54, 0, new[] { 53, 57, 60, 64 })]
    public void A_played_note_picks_the_chord_and_the_root_moves_it_on(
        string scale, int tonic, int note, int root, int[] notes)
    {
        Heard(b =>
        {
            var chord = b.Add(NodeCatalog.AutoChordTypeId, (0, tonic), (1, root), (2, note));
            b.Wire(b.Add("value", (0, (float)note)), 0, chord, 2);
            Scaled(chord, scale);
            return chord;
        }, 0).ShouldBe(notes.Select(n => (double)Pitch.Frequency(n)).ToArray(), 1e-3);
    }

    [Fact]
    public void A_played_note_plays_every_chord_of_every_scale()
    {
        foreach (var scale in Chords.Scales)
        {
            var program = Compiled(b =>
            {
                var chord = b.Add(NodeCatalog.AutoChordTypeId, (0, 62f));
                Scaled(chord, scale.Id);
                b.Wire(b.Add("coord"), 0, chord, 2);
                return chord;
            });

            for (var note = 30; note <= 100; note++)
            {
                var root = Chords.Steps(scale, note - 62);

                Heard(program, note).ShouldBe(Notes(62, Chords.Diatonic(scale, root)), 1e-3, $"{scale.Name} on {note}");
            }
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(-1, -1)]
    [InlineData(11, 6)]
    [InlineData(12, 7)]
    [InlineData(-13, -8)]
    public void A_note_counts_as_the_steps_of_the_nearest_note_on_the_scale(int semitones, int steps) =>
        Chords.Steps(Chords.Scale("ionian"), semitones).ShouldBe(steps);

    [Fact]
    public void A_patched_root_is_rounded_to_a_step() =>
        Heard(b =>
        {
            var chord = b.Add(NodeCatalog.AutoChordTypeId, (0, 60f));
            b.Wire(b.Add("coord"), 0, chord, 1);
            return chord;
        }, 0.6).ShouldBe(Notes(60, [2, 5, 9, 12]), 1e-3);

    // --- harness -----------------------------------------------------------------

    private static void Scaled(NodeInstance chord, string scale) =>
        chord.SetState(NodeCatalog.AutoChordStateKey, new JsonObject { [NodeCatalog.AutoChordScaleField] = scale });

    private static double[] Notes(int root, int[] above) =>
        [.. above.Select(n => (double)Pitch.Frequency(root + n))];

    /// <summary>The four outputs, each through the left speaker of a patch of its own.</summary>
    private static List<CompiledPatch> Compiled(Func<PatchBuilder, NodeInstance> build)
    {
        var programs = new List<CompiledPatch>();

        for (var output = 0; output < Chords.Notes; output++)
        {
            var b = new PatchBuilder(NodeCatalog.BuiltIn);
            var module = build(b);
            var speakers = b.Add(NodeCatalog.OutputTypeId, (NodeCatalog.OutputVolumePort, 1f));
            b.Wire(module, output, speakers, NodeCatalog.OutputLeftPort);

            var result = b.Patch.CompileForAudio(NodeCatalog.BuiltIn);
            result.HasErrors.ShouldBeFalse(string.Join("; ", result.Issues.Select(i => i.Message)));
            programs.Add(result.Program);
        }

        return programs;
    }

    private static double[] Heard(Func<PatchBuilder, NodeInstance> build, double x) => Heard(Compiled(build), x);

    private static double[] Heard(List<CompiledPatch> programs, double x) =>
    [
        .. programs.Select(program =>
        {
            var state = new DelayState(program.DelayLengths, Rate, program.PhaseCount, program.UnitCount);
            var registers = program.AllocateRegisters();

            program.Evaluate(x, 0d, 0d, registers, default, state);

            return registers[program.OutputBase];
        }),
    ];
}
