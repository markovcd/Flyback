using System.Text.Json.Nodes;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;
using Flyback.Engine.Compile;
using Shouldly;

namespace Flyback.Core.Tests.Compile;

/// <summary>
/// A polyphonic wire (ADR-0174): what is downstream of a module that starts one
/// is lowered once per voice, each with memory of its own, until something
/// merges the voices into one.
/// </summary>
public class PolyphonicWireTests
{
    private const int A = 0;
    private const int B = 1;

    private static NodeInstance Voices(PatchBuilder b, string typeId, int count, params (int Port, float Value)[] knobs)
    {
        var node = b.Add(typeId, 0, 0, knobs);

        node.SetState("voices", new JsonObject { ["voices"] = (float)count });
        return node;
    }

    private static NodeInstance Output(PatchBuilder b) =>
        b.Add(NodeCatalog.OutputTypeId, 0, 0, (NodeCatalog.OutputVolumePort, 1f));

    /// <summary>The first evaluation of the speakers' program, left side.</summary>
    private static double Heard(Patch patch)
    {
        var result = patch.CompileForAudio(NodeCatalog.BuiltIn);
        result.HasErrors.ShouldBeFalse();

        var registers = result.Program.AllocateRegisters();
        result.Program.Evaluate(0d, 0d, 0d, registers, default);

        return registers[result.Program.OutputBase];
    }

    [Fact]
    public void The_output_hears_a_polyphonic_wire_with_its_voices_added()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var voice = Voices(b, NodeCatalog.VoiceTypeId, 4);

        b.Wire(voice, 0, Output(b), NodeCatalog.OutputLeftPort);

        Heard(b.Patch).ShouldBe(0d + 1d + 2d + 3d);
    }

    [Fact]
    public void A_one_voice_wire_reaches_every_voice_of_a_module_it_meets()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var voice = Voices(b, NodeCatalog.VoiceTypeId, 3);
        var add = b.Add("math.add", 0, 0, (B, 10f));

        b.Wire(voice, 0, add, A).Wire(add, 0, Output(b), NodeCatalog.OutputLeftPort);

        Heard(b.Patch).ShouldBe(0d + 1d + 2d + 3 * 10d);
    }

    /// <summary>The shorter wire is silent in the voices it lacks, so nothing is counted twice.</summary>
    [Fact]
    public void Two_wires_of_different_counts_run_the_larger_and_the_shorter_is_silent_past_its_end()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var two = Voices(b, NodeCatalog.VoiceTypeId, 2);
        var four = Voices(b, NodeCatalog.VoiceTypeId, 4);
        var add = b.Add("math.add", 0, 0);

        b.Wire(two, 0, add, A).Wire(four, 0, add, B).Wire(add, 0, Output(b), NodeCatalog.OutputLeftPort);

        Heard(b.Patch).ShouldBe((0d + 1d) + (0d + 1d + 2d + 3d));
    }

    [Fact]
    public void Spread_steps_each_voice_by_its_offset()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var spread = Voices(b, NodeCatalog.SpreadTypeId, 3, (0, 60f), (1, 4f));

        b.Wire(spread, 0, Output(b), NodeCatalog.OutputLeftPort);

        Heard(b.Patch).ShouldBe(60d + 64d + 68d);
    }

    /// <summary>
    /// Each voice of an oscillator keeps a phase of its own, and what follows a
    /// Merge is one module again however many voices went in.
    /// </summary>
    [Fact]
    public void A_chain_runs_once_per_voice_up_to_a_merge_and_once_after_it()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var spread = Voices(b, NodeCatalog.SpreadTypeId, 4, (0, 110f), (1, 55f));
        var tone = b.Add(NodeCatalog.SineTypeId, 0, 0);
        var merge = b.Add(NodeCatalog.MergeTypeId, 0, 0);
        var wobble = b.Add(NodeCatalog.SineTypeId, 0, 0);

        b.Wire(spread, 0, tone, 1)
            .Wire(tone, 0, merge, 0)
            .Wire(merge, 0, wobble, 1)
            .Wire(wobble, 0, Output(b), NodeCatalog.OutputLeftPort);

        var program = b.Patch.CompileForAudio(NodeCatalog.BuiltIn).Program;

        program.PhaseCount.ShouldBe(4 + 1);
    }

    /// <summary>Played for a while, four voices of one Sine are four Sines.</summary>
    [Fact]
    public void Each_voice_sounds_as_its_own_module_would()
    {
        var poly = new PatchBuilder(NodeCatalog.BuiltIn);
        var spread = Voices(poly, NodeCatalog.SpreadTypeId, 4, (0, 110f), (1, 55f));
        var tone = poly.Add(NodeCatalog.SineTypeId, 0, 0);

        poly.Wire(spread, 0, tone, 1).Wire(tone, 0, Output(poly), NodeCatalog.OutputLeftPort);

        var copies = new PatchBuilder(NodeCatalog.BuiltIn);
        var sum = Output(copies);
        NodeInstance? previous = null;

        foreach (var hz in (float[])[110f, 165f, 220f, 275f])
        {
            var sine = copies.Add(NodeCatalog.SineTypeId, 0, 0, (1, hz));

            if (previous is null)
            {
                previous = sine;
                continue;
            }

            var add = copies.Add("math.add", 0, 0);

            copies.Wire(previous, 0, add, A).Wire(sine, 0, add, B);
            previous = add;
        }

        copies.Wire(previous!, 0, sum, NodeCatalog.OutputLeftPort);

        var heard = Played(poly.Patch, 2_000);
        var expected = Played(copies.Patch, 2_000);

        for (var i = 0; i < heard.Length; i++) heard[i].ShouldBe(expected[i], 1e-9, $"sample {i}");
    }

    /// <summary>
    /// A loop round a polyphonic chain carries each voice's evaluation before in
    /// a plane of its own, so voices that count at different rates stay apart.
    /// </summary>
    [Fact]
    public void A_loop_round_a_polyphonic_chain_keeps_each_voice_apart()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var voice = Voices(b, NodeCatalog.VoiceTypeId, 3);
        var add = b.Add("math.add", 0, 0);

        b.Wire(voice, 0, add, A).Wire(add, 0, add, B).Wire(add, 0, Output(b), NodeCatalog.OutputLeftPort);

        var heard = Played(b.Patch, 5);

        // Voice v holds v × n after n evaluations, and the three add up.
        heard.ShouldBe([3d, 6d, 9d, 12d, 15d]);
    }

    [Fact]
    public void A_reverb_hears_the_voices_added_and_is_one_reverb()
    {
        var mono = new PatchBuilder(NodeCatalog.BuiltIn);
        var one = mono.Add("math.add", 0, 0);
        var room = mono.Add(NodeCatalog.ReverbTypeId, 0, 0);

        mono.Wire(one, 0, room, 0).Wire(room, 0, Output(mono), NodeCatalog.OutputLeftPort);

        var poly = new PatchBuilder(NodeCatalog.BuiltIn);
        var voices = Voices(poly, NodeCatalog.VoiceTypeId, 4);
        var polyRoom = poly.Add(NodeCatalog.ReverbTypeId, 0, 0);

        poly.Wire(voices, 0, polyRoom, 0).Wire(polyRoom, 0, Output(poly), NodeCatalog.OutputLeftPort);

        poly.Patch.CompileForAudio(NodeCatalog.BuiltIn).Program.DelayLengths.Count
            .ShouldBe(mono.Patch.CompileForAudio(NodeCatalog.BuiltIn).Program.DelayLengths.Count);
    }

    [Fact]
    public void A_polyphonic_midi_in_reads_one_voice_per_voice_of_its_wire()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var midi = b.Add(NodeCatalog.MidiTypeId, 0, 0);

        midi.SetState(MidiExtra.StateKey, new JsonObject { [MidiExtra.VoicesField] = 3f });
        b.Wire(midi, 0, Output(b), NodeCatalog.OutputLeftPort);

        var program = b.Patch.CompileForAudio(NodeCatalog.BuiltIn).Program;
        var live = new LiveValues(program.LiveInputs);

        live.Set(MidiSignal.Key(MidiSources.Keyboard, 1, MidiSignal.Pitch), 60f);
        live.Set(MidiSignal.Key(MidiSources.Keyboard, 2, MidiSignal.Pitch), 64f);
        live.Set(MidiSignal.Key(MidiSources.Keyboard, 3, MidiSignal.Pitch), 67f);

        var registers = program.AllocateRegisters();
        program.Evaluate(0d, 0d, 0d, registers, default, live: live);

        registers[program.OutputBase].ShouldBe(60d + 64d + 67d);
        program.LiveInputs.ShouldNotContain(key => key.Contains("/auto/"));
    }

    [Fact]
    public void A_midi_in_from_a_high_voice_counts_up_and_stops_at_the_last()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var midi = b.Add(NodeCatalog.MidiTypeId, 0, 0);

        midi.SetState(MidiExtra.StateKey, new JsonObject
        {
            [MidiExtra.IndexField] = 7f,
            [MidiExtra.VoicesField] = 4f,
        });
        b.Wire(midi, 0, Output(b), NodeCatalog.OutputLeftPort);

        b.Patch.CompileForAudio(NodeCatalog.BuiltIn).Program.LiveInputs
            .Where(key => key.EndsWith('/' + MidiSignal.Pitch))
            .ShouldBe(
                [
                    MidiSignal.Key(MidiSources.Keyboard, 7, MidiSignal.Pitch),
                    MidiSignal.Key(MidiSources.Keyboard, 8, MidiSignal.Pitch),
                ],
                ignoreOrder: true);
    }

    /// <summary>A module that says something says it once, not once a voice.</summary>
    [Fact]
    public void A_warning_is_said_once_however_many_voices_say_it()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var midi = b.Add(NodeCatalog.MidiTypeId, 0, 0);

        midi.SetState(MidiExtra.StateKey, new JsonObject
        {
            [MidiExtra.DeviceField] = "midi:nowhere",
            [MidiExtra.VoicesField] = 4f,
        });
        b.Wire(midi, 0, Output(b), NodeCatalog.OutputLeftPort);

        b.Patch.CompileForAudio(NodeCatalog.BuiltIn).Issues
            .Count(issue => issue.Message.Contains("midi:nowhere")).ShouldBe(1);
    }

    /// <summary>
    /// A plugin's module writes ops like a built-in one and keeps its memory in
    /// the program, so it is lowered once per voice with nothing declared.
    /// </summary>
    [Fact]
    public void A_plugins_module_is_lowered_once_per_voice_with_memory_of_its_own()
    {
        var provider = new ModuleProvider("sample", "Sample");
        var echo = new NodeDef(
            "sample.echo", "Echo", ModuleCategories.TimeEffects,
            [new PortSpec("in") { Help = "What echoes." }],
            [new PortSpec("out") { Help = "The echo." }],
            (em, i) => [em.DelayLine(OpCode.Delay, i[0], em.Constant(0f), em.Constant(0.1f), 1f)]);
        var catalog = NodeCatalog.BuiltIn.With(provider, [echo]).Catalog;

        var b = new PatchBuilder(catalog);
        var voice = Voices(b, NodeCatalog.VoiceTypeId, 3);
        var module = b.Add("sample.echo", 0, 0);

        b.Wire(voice, 0, module, 0).Wire(module, 0, Output(b), NodeCatalog.OutputLeftPort);

        b.Patch.CompileForAudio(catalog).Program.DelayLengths.Count.ShouldBe(3);
    }

    [Fact]
    public void The_picture_sees_a_polyphonic_wire_with_its_voices_added()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var voice = Voices(b, NodeCatalog.VoiceTypeId, 3);
        var add = b.Add("math.add", 0, 0, (B, 0.25f));

        b.Wire(voice, 0, add, A).Wire(add, 0, Output(b), NodeCatalog.OutputColorPort);

        var program = b.Patch.CompileForVideo(NodeCatalog.BuiltIn).Program;
        var registers = program.AllocateRegisters();
        program.Evaluate(0d, 0d, 0d, registers, default);

        registers[program.OutputBase].ShouldBe(0d + 1d + 2d + 3 * 0.25d);
    }

    /// <summary>
    /// The IL backend sees an ordinary program, so it agrees with the
    /// interpreter bit for bit as it does on any other (ADR-0035).
    /// </summary>
    [Fact]
    public void The_il_plays_a_polyphonic_patch_as_the_interpreter_does()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var spread = Voices(b, NodeCatalog.SpreadTypeId, 5, (0, 110f), (1, 37f));
        var tone = b.Add(NodeCatalog.SineTypeId, 0, 0);
        var delay = b.Add(NodeCatalog.DelayTypeId, 0, 0, (1, 0.01f));

        b.Wire(spread, 0, tone, 1).Wire(tone, 0, delay, 0).Wire(delay, 0, Output(b), NodeCatalog.OutputLeftPort);

        var program = b.Patch.CompileForAudio(NodeCatalog.BuiltIn).Program;
        var il = IlProgram.Compile(program);

        var expectedMemory = new DelayState(program, 48_000);
        var actualMemory = new DelayState(program, 48_000);
        var expected = program.AllocateRegisters();
        var actual = program.AllocateRegisters();

        for (var i = 0; i < 2_000; i++)
        {
            var t = i / 192_000d;

            program.Evaluate(0d, 0d, t, expected, default, expectedMemory);
            il.Evaluate(0d, 0d, t, actual, default, actualMemory);

            BitConverter.DoubleToInt64Bits(actual[program.OutputBase])
                .ShouldBe(BitConverter.DoubleToInt64Bits(expected[program.OutputBase]), $"sample {i}");
        }
    }

    /// <summary>The left side, evaluation by evaluation, with memory.</summary>
    private static double[] Played(Patch patch, int evaluations)
    {
        var result = patch.CompileForAudio(NodeCatalog.BuiltIn);
        result.HasErrors.ShouldBeFalse();

        var program = result.Program;
        var memory = new DelayState(program, 48_000);
        var registers = program.AllocateRegisters();
        var heard = new double[evaluations];

        for (var i = 0; i < evaluations; i++)
        {
            program.Evaluate(0d, 0d, i / 192_000d, registers, default, memory);
            heard[i] = registers[program.OutputBase];
        }

        return heard;
    }
}
