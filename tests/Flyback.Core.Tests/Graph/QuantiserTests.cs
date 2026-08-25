using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// The Quantiser: the nearest note of a scale to whatever arrives, where the
/// scale is a set of pitch classes carried on the node rather than wired into
/// it.
/// </summary>
/// <remarks>
/// Like <see cref="NoteTests"/>, these check arithmetic that has a right answer
/// outside the program — the nearest C to a given number is the nearest C
/// whether or not anything renders it — so they are worked out here and compared
/// against what the ops produce.
/// <para>
/// What is peculiar to this module is that the scale decides the shape of the
/// program and not only its result: a note switched off contributes no ops at
/// all, and the two ends of the range are special cases. So the op counts are
/// checked as well as the values.
/// </para>
/// </remarks>
public class QuantiserTests
{
    private static readonly int[] Major = [0, 2, 4, 5, 7, 9, 11];

    /// <summary>C, E and G — a triad, and the smallest scale worth the name.</summary>
    private static readonly int[] Triad = [0, 4, 7];

    private static readonly int[] Chromatic = [.. Enumerable.Range(0, Pitch.Classes)];

    /// <summary>
    /// A patch of one Quantiser reading its knob, compiled for the speakers —
    /// the shortest way to get a scalar out — and evaluated once.
    /// </summary>
    private static (double Note, int Ops) Snap(float value, IEnumerable<int>? scale)
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);

        var quantiser = builder.Add(NodeCatalog.QuantiserTypeId, 0, 0, (0, value));
        quantiser.Scale = scale is null ? null : [.. scale];

        var sink = builder.Add(NodeCatalog.OutputTypeId, 0, 0, (NodeCatalog.OutputGainPort, 1f));
        builder.Wire(quantiser, 0, sink, NodeCatalog.OutputLeftPort);

        var program = builder.Patch.CompileForAudio(NodeCatalog.BuiltIn).Program;
        var registers = program.AllocateRegisters();
        program.Evaluate(0f, 0f, 0f, registers, default);

        return (registers[program.OutputBase], program.Ops.Length);
    }

    private static double Note(float value, IEnumerable<int>? scale) => Snap(value, scale).Note;

    /// <summary>
    /// What the module is for, worked out by hand: 60 is middle C, and a major
    /// scale on C has no C# in it — so anything between C and D is pulled to
    /// whichever of the two it is nearer.
    /// </summary>
    [Theory]
    [InlineData(60f, 60f)]
    [InlineData(60.4f, 60f)]
    [InlineData(61f, 62f)]
    [InlineData(61.4f, 62f)]
    [InlineData(62f, 62f)]
    [InlineData(62.9f, 62f)]
    [InlineData(63.6f, 64f)]
    public void A_value_lands_on_the_nearest_note_the_scale_has(float value, float expected)
    {
        Note(value, Major).ShouldBe(expected);
    }

    /// <summary>
    /// A pitch class is every octave of that note at once, which is what makes a
    /// scale a scale rather than a list of notes. The same C-major scale catches
    /// values two octaves down and three up without being told about them, and
    /// goes on catching them below zero — note -1 is a B, so a scale with B in
    /// it leaves that one alone.
    /// </summary>
    [Theory]
    [InlineData(36.4f, 36f)]
    [InlineData(37f, 38f)]
    [InlineData(96.4f, 96f)]
    [InlineData(-1f, -1f)]
    [InlineData(-13f, -13f)]
    [InlineData(-2.4f, -3f)]
    public void The_scale_repeats_in_every_octave(float value, float expected)
    {
        Note(value, Major).ShouldBe(expected);
    }

    /// <summary>
    /// The gaps a sparse scale leaves are the point of it. C, E and G with
    /// nothing between them means a value at D is nearer E than C by a whisker
    /// and lands there, and the widest gap — G up to the next C — is five
    /// semitones with its boundary in the middle.
    /// </summary>
    [Theory]
    [InlineData(60f, 60f)]
    [InlineData(61.9f, 60f)]
    [InlineData(62.1f, 64f)]
    [InlineData(65.4f, 64f)]
    [InlineData(65.6f, 67f)]
    [InlineData(69.4f, 67f)]
    [InlineData(69.6f, 72f)]
    public void A_sparse_scale_snaps_across_its_gaps(float value, float expected)
    {
        Note(value, Triad).ShouldBe(expected);
    }

    /// <summary>
    /// Every note switched on is the nearest semitone, which is what a Note
    /// module does on its own — and the module says so in two ops rather than in
    /// twelve candidates.
    /// </summary>
    [Fact]
    public void A_chromatic_scale_is_the_nearest_semitone_and_costs_almost_nothing()
    {
        foreach (var value in new[] { 57f, 57.2f, 57.5f, 60.9f, -0.5f, -1.4f })
            Note(value, Chromatic).ShouldBe(Pitch.Nearest(value));

        var (_, ops) = Snap(57.2f, Chromatic);
        var (_, bare) = Snap(57.2f, []);
        var (_, major) = Snap(57.2f, Major);

        // An add and a floor, and the half they need — a literal is an op here
        // like anything else. Against a seven-note scale, which is what this
        // would otherwise cost twelve notes' worth of.
        (ops - bare).ShouldBe(3);
        ops.ShouldBeLessThan(major);
    }

    /// <summary>
    /// Nothing switched on is a wire. There is no note to snap to, so what comes
    /// out is what went in — which is what makes the twelve switches safe to
    /// turn off one at a time.
    /// </summary>
    [Theory]
    [InlineData(57.3f)]
    [InlineData(-4.8f)]
    public void An_empty_scale_passes_the_signal_through(float value)
    {
        Note(value, []).ShouldBe(value, 1e-5);
        Note(value, null).ShouldBe(value, 1e-5);
    }

    /// <summary>
    /// A note switched off costs nothing at all, which is the whole reason the
    /// scale is a compile-time value rather than a signal — see
    /// <see cref="EmitContext.Scale"/>.
    /// </summary>
    [Fact]
    public void A_note_switched_off_contributes_no_ops()
    {
        var (_, three) = Snap(60f, Triad);
        var (_, seven) = Snap(60f, Major);

        three.ShouldBeLessThan(seven);

        // Each note after the first is one candidate and one comparison, and
        // both are a fixed number of ops — so the cost is linear in how many are
        // switched on rather than in how many there could be.
        var (_, four) = Snap(60f, [0, 4, 7, 11]);

        (four - three).ShouldBe((seven - three) / 4);
    }

    /// <summary>
    /// A scale is a set. A file naming a note twice, or naming a thirteenth, is
    /// the only way either arrives — and both are tidied on the way in rather
    /// than lowered into candidates that duplicate or land outside the octave.
    /// </summary>
    [Fact]
    public void A_scale_from_a_hand_edited_file_is_held_to_what_a_scale_is()
    {
        Pitch.Scale([4, 0, 4, 7, 0]).ShouldBe(Triad);
        Pitch.Scale([0, 12, 13, -1, 7, 4]).ShouldBe(Triad);
        Pitch.Scale(null).ShouldBeEmpty();

        // And the compiler agrees: the untidied scale costs what the tidy one
        // does, rather than two ops per repeat.
        Snap(60f, [4, 0, 4, 7, 0, 12, -1]).Ops.ShouldBe(Snap(60f, Triad).Ops);
        Note(62.1f, [4, 0, 4, 7, 0, 12, -1]).ShouldBe(64f);
    }

    /// <summary>
    /// Exactly halfway between two of the scale's notes is a boundary, and which
    /// side of it a value falls on is settled the same way twice: the higher of
    /// the two, which is also how <see cref="Pitch.Nearest"/> breaks a tie.
    /// </summary>
    [Fact]
    public void A_tie_goes_to_the_higher_note()
    {
        Note(61f, Major).ShouldBe(62f);
        Note(66f, Triad).ShouldBe(67f);
        Note(70.5f, Triad).ShouldBe(72f);
    }

    /// <summary>
    /// A freshly placed one carries a major scale, so it does something audible
    /// the moment it is put down.
    /// </summary>
    [Fact]
    public void A_new_one_arrives_with_a_major_scale_on_it()
    {
        var def = NodeCatalog.BuiltIn.Require(NodeCatalog.QuantiserTypeId);
        var node = NodeInstance.Create(def, 0, 0);

        node.Scale.ShouldBe(Major);

        // Its own copy: turning a note off on one must not turn it off on the
        // definition every other instance is made from.
        node.Scale!.Remove(0);
        NodeInstance.Create(def, 0, 0).Scale.ShouldBe(Major);
    }

    /// <summary>
    /// The preset the module ships with, checked for the one thing a snapshot
    /// cannot see: that what comes out is a tune in the scale rather than a
    /// picture of one. A preset that looks right and plays wrong is exactly the
    /// mistake nothing else here would catch.
    /// </summary>
    [Fact]
    public void The_preset_plays_nothing_that_is_not_in_its_scale()
    {
        var patch = Presets.InKey(NodeCatalog.BuiltIn);

        var quantiser = patch.Nodes.Single(n => n.TypeId == NodeCatalog.QuantiserTypeId);
        var scale = quantiser.Scale.ShouldNotBeNull();

        // Rooted at the Quantiser rather than at the Output, which is what lets
        // the note be read as a number instead of inferred from a waveform.
        var program = patch.CompileForProbe(quantiser.Id, NodeCatalog.BuiltIn).Program;
        var registers = program.AllocateRegisters();

        var played = new HashSet<float>();

        for (var i = 0; i < 300; i++)
        {
            program.Evaluate(0f, 0f, i * 0.1f, registers, default);
            played.Add((float)registers[program.OutputBase]);
        }

        // Half a minute of it, and every note of it in the scale.
        foreach (var note in played)
        {
            note.ShouldBe(MathF.Floor(note), $"{note} is not a whole note");
            scale.ShouldContain((int)(((note % Pitch.Classes) + Pitch.Classes) % Pitch.Classes));
        }

        // And a melody rather than one held note: the field wanders across most
        // of the two octaves it was given.
        played.Count.ShouldBeGreaterThan(6);
    }

    /// <summary>
    /// The defect this preset was heard to have, and the one nothing looking at
    /// the patch would find: every note in it was cleanly snapped and it still
    /// glided, because the pitch was free to change while a note was sounding.
    /// </summary>
    /// <remarks>
    /// A clean step in the middle of a held note is heard as a slide to the next
    /// one — ADR-0030 is what stops that clicking, and what is left when it does
    /// not click is a glide. So the test is not that the pitch steps, which it
    /// always did, but <em>when</em>: every change has to land where nothing is
    /// sounding.
    /// <para>
    /// The envelope is stepped in order rather than sampled, because it has
    /// memory. Reading it at scattered moments would answer with whatever the
    /// last evaluation left behind.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_preset_only_changes_pitch_while_nothing_is_sounding()
    {
        const int rate = 48_000;

        var patch = Presets.InKey(NodeCatalog.BuiltIn);

        var quantiser = patch.Nodes.Single(n => n.TypeId == NodeCatalog.QuantiserTypeId);
        var envelope = patch.Nodes.Single(n => n.TypeId == NodeCatalog.AdsrTypeId);

        var pitch = patch.CompileForProbe(quantiser.Id, NodeCatalog.BuiltIn).Program;
        var level = patch.CompileForProbe(envelope.Id, NodeCatalog.BuiltIn).Program;

        var pitchRegisters = pitch.AllocateRegisters();
        var levelRegisters = level.AllocateRegisters();
        var memory = new DelayState(level.DelayLengths, rate, level.PhaseCount, level.UnitCount);

        var previous = 0d;
        var changes = 0;
        var loudest = 0d;

        for (var i = 0; i < rate * 20; i++)
        {
            var t = (float)(i / (double)rate);

            level.Evaluate(0f, 0f, t, levelRegisters, default, memory);
            pitch.Evaluate(0f, 0f, t, pitchRegisters, default);

            var note = pitchRegisters[pitch.OutputBase];

            if (i > 0 && note != previous)
            {
                changes++;
                loudest = Math.Max(loudest, levelRegisters[level.OutputBase]);
            }

            previous = note;
        }

        // A tune rather than one held note, and not one of its changes audible.
        changes.ShouldBeGreaterThan(10);
        loudest.ShouldBe(0d);
    }

    /// <summary>
    /// The picture and the sound agree, because there is nothing here to
    /// disagree about: no memory, no domain, and one evaluation is the whole of
    /// what it does.
    /// </summary>
    [Fact]
    public void It_reads_the_same_on_both_sinks()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);

        var quantiser = builder.Add(NodeCatalog.QuantiserTypeId, 0, 0, (0, 61.4f));
        quantiser.Scale = [.. Major];

        var sink = builder.Add(NodeCatalog.OutputTypeId, 0, 0, (NodeCatalog.OutputGainPort, 1f));
        builder.Wire(quantiser, 0, sink, NodeCatalog.OutputLeftPort);
        builder.Wire(quantiser, 0, sink, NodeCatalog.OutputColorPort);

        var audio = builder.Patch.CompileForAudio(NodeCatalog.BuiltIn).Program;
        var video = builder.Patch.CompileForVideo(NodeCatalog.BuiltIn).Program;

        double Read(CompiledPatch program)
        {
            var registers = program.AllocateRegisters();
            program.Evaluate(0f, 0f, 0f, registers, default);
            return registers[program.OutputBase];
        }

        Read(audio).ShouldBe(62f);
        Read(video).ShouldBe(62f);
    }
}
