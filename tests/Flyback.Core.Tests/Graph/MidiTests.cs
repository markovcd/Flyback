using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// The MIDI In is the one module whose answer comes from outside the patch, so
/// these are about the seam rather than about arithmetic: that what a program
/// asks for is what somebody playing can fill in, that a note struck becomes an
/// edge on the path that has a memory and nothing at all on the path that has
/// none, and that a device nobody can find is said rather than swallowed.
/// </summary>
public class MidiTests
{
    private const int Pitch = 0;
    private const int Gate = 1;
    private const int Velocity = 2;
    private const int Trigger = 3;

    /// <summary>A patch of one MIDI In, wired to the ear through the given output.</summary>
    private static CompiledPatch Heard(int port, string? device = null) =>
        Built(port, device).CompileForAudio(NodeCatalog.BuiltIn).Program;

    private static CompiledPatch Seen(int port, string? device = null) =>
        Built(port, device).CompileForVideo(NodeCatalog.BuiltIn).Program;

    private static Patch Built(int port, string? device)
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        var midi = builder.Add(NodeCatalog.MidiTypeId, 0, 0);
        midi.SetState(MidiExtra.StateKey, new System.Text.Json.Nodes.JsonObject
        {
            [MidiExtra.IndexField] = 1f,
        });
        var sink = builder.Add(NodeCatalog.OutputTypeId, 0, 0, (NodeCatalog.OutputGainPort, 1f));

        if (device is not null)
            midi.SetState(MidiExtra.StateKey, new System.Text.Json.Nodes.JsonObject
            {
                [MidiExtra.DeviceField] = device,
                [MidiExtra.IndexField] = 1f,
            });

        // Both sinks at once: the same signal into the color and into the left
        // channel, so one patch answers for the eye and the ear and neither is a
        // different patch that happens to look alike.
        builder.Wire(midi, port, sink, NodeCatalog.OutputColorPort);
        builder.Wire(midi, port, sink, NodeCatalog.OutputLeftPort);

        return builder.Patch;
    }

    /// <summary>
    /// Runs a program for as many evaluations as there are readings wanted, with
    /// the block filled in before each one, and hands back what came out.
    /// </summary>
    private static double[] Play(
        CompiledPatch program,
        DelayState? memory,
        params Action<LiveValues>[] moments)
    {
        var live = new LiveValues(program.LiveInputs);
        var registers = program.AllocateRegisters();
        var heard = new double[moments.Length];

        for (var i = 0; i < moments.Length; i++)
        {
            moments[i](live);
            program.Evaluate(0d, 0d, i / (double)GlobalConstants.SampleRate, registers, default, memory, live: live);
            heard[i] = registers[program.OutputBase];
        }

        return heard;
    }

    private static DelayState Memory(CompiledPatch program) =>
        new(program.DelayLengths, 48_000, program.PhaseCount, program.UnitCount, program.TraceCount);

    /// <summary>Nothing changes, so every evaluation is the one before it.</summary>
    private static Action<LiveValues> Held(float pitch, float gate, float velocity, float strikes) =>
        live =>
        {
            live.Set(MidiSignal.Key(MidiSources.Keyboard, MidiSignal.Pitch), pitch);
            live.Set(MidiSignal.Key(MidiSources.Keyboard, MidiSignal.Gate), gate);
            live.Set(MidiSignal.Key(MidiSources.Keyboard, MidiSignal.Velocity), velocity);
            live.Set(MidiSignal.Key(MidiSources.Keyboard, MidiSignal.Strikes), strikes);
        };

    private static Action<LiveValues> Silent() => Held(60f, 0f, 0f, 0f);

    [Fact]
    public void A_program_holding_one_says_which_instrument_it_is_played_with()
    {
        var program = Heard(Pitch);

        program.LiveInputs.ShouldBe(
            [
                MidiSignal.Key(MidiSources.Keyboard, MidiSignal.Pitch),
                MidiSignal.Key(MidiSources.Keyboard, MidiSignal.Gate),
                MidiSignal.Key(MidiSources.Keyboard, MidiSignal.Velocity),
                MidiSignal.Key(MidiSources.Keyboard, MidiSignal.Strikes),
            ],
            ignoreOrder: true);
    }

    /// <summary>Every other patch in the catalogue is played by nothing at all.</summary>
    [Fact]
    public void A_patch_without_one_asks_for_nothing()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        var saw = builder.Add("osc.saw", 0, 0);
        var sink = builder.Add(NodeCatalog.OutputTypeId, 0, 0, (NodeCatalog.OutputGainPort, 1f));
        builder.Wire(saw, 0, sink, NodeCatalog.OutputLeftPort);

        builder.Patch.CompileForAudio(NodeCatalog.BuiltIn).Program.LiveInputs.ShouldBeEmpty();
    }

    [Fact]
    public void The_note_being_held_is_what_comes_out_of_pitch()
    {
        var program = Heard(Pitch);

        Play(program, Memory(program), Held(64f, 1f, 0.5f, 1f), Held(67f, 1f, 0.5f, 2f))
            .ShouldBe([64d, 67d]);
    }

    [Fact]
    public void Velocity_is_what_the_key_was_struck_with()
    {
        var program = Heard(Velocity);

        Play(program, Memory(program), Held(64f, 1f, 0.25f, 1f)).ShouldBe([0.25d]);
    }

    /// <summary>
    /// The whole of what the module adds to four table reads: a count that only
    /// goes up, differenced into a pulse one evaluation wide.
    /// </summary>
    [Fact]
    public void A_note_struck_is_one_evaluation_of_trigger()
    {
        var program = Heard(Trigger);

        Play(
            program,
            Memory(program),
            Silent(),
            Held(64f, 1f, 1f, 1f),
            Held(64f, 1f, 1f, 1f),
            Held(67f, 1f, 1f, 2f),
            Held(67f, 1f, 1f, 2f))
            .ShouldBe([0d, 1d, 0d, 1d, 0d]);
    }

    /// <summary>
    /// The gap the trigger cuts in the gate, which is what makes an envelope
    /// articulate the second note of a legato run rather than sliding through it.
    /// </summary>
    [Fact]
    public void The_gate_closes_for_the_evaluation_a_new_note_lands_on()
    {
        var program = Heard(Gate);

        Play(
            program,
            Memory(program),
            Silent(),
            Held(64f, 1f, 1f, 1f),
            Held(64f, 1f, 1f, 1f),
            Held(67f, 1f, 1f, 2f),
            Held(67f, 1f, 1f, 2f))
            .ShouldBe([0d, 0d, 1d, 0d, 1d]);
    }

    /// <summary>
    /// A count that has been going up all session is not a note struck now. The
    /// first evaluation has nothing to difference against, and answering
    /// otherwise would fire a trigger at every rewind.
    /// </summary>
    [Fact]
    public void A_program_that_starts_mid_session_does_not_trigger_on_the_first_evaluation()
    {
        var program = Heard(Trigger);

        Play(program, Memory(program), Held(64f, 1f, 1f, 900f), Held(64f, 1f, 1f, 900f))
            .ShouldBe([0d, 0d]);
    }

    /// <summary>
    /// A tally is not a signal: a session past its sixteenth note must go on
    /// triggering, which it would not if the count were held to the rails the way
    /// a value coming round a loop is. See ClockWrite.
    /// </summary>
    [Fact]
    public void The_count_goes_on_meaning_something_past_the_rails()
    {
        var program = Heard(Trigger);

        Play(
            program,
            Memory(program),
            Held(64f, 1f, 1f, 200f),
            Held(64f, 1f, 1f, 200f),
            Held(67f, 1f, 1f, 201f),
            Held(67f, 1f, 1f, 201f))
            .ShouldBe([0d, 0d, 1d, 0d]);
    }

    /// <summary>
    /// A picture is one evaluation with nothing before it, so there is no
    /// previous count to have moved from. The chosen answer is no strike rather
    /// than the emergent one, which would be a trigger stuck high and a gate held
    /// shut across the whole screen from the first note of the session.
    /// </summary>
    [Fact]
    public void The_picture_is_given_no_trigger_and_an_open_gate()
    {
        var trigger = Seen(Trigger);
        var gate = Seen(Gate);

        Play(trigger, null, Held(64f, 1f, 1f, 7f)).ShouldBe([0d]);
        Play(gate, null, Held(64f, 1f, 1f, 7f)).ShouldBe([1d]);
    }

    /// <summary>
    /// What the eye sees is what the ear hears, which is the point of playing the
    /// picture at all — a patch is one instrument with two sinks.
    /// </summary>
    [Fact]
    public void The_picture_hears_the_same_note_the_speakers_do()
    {
        var seen = Seen(Pitch);

        Play(seen, null, Held(72f, 1f, 1f, 3f)).ShouldBe([72d]);
    }

    /// <summary>
    /// Nobody at the keys is the ordinary case offline, and it is silence rather
    /// than a fault.
    /// </summary>
    [Fact]
    public void A_program_run_with_no_block_at_all_reads_nothing()
    {
        var program = Heard(Pitch);
        var registers = program.AllocateRegisters();

        program.Evaluate(0d, 0d, 0d, registers, default);

        registers[program.OutputBase].ShouldBe(0d);
    }

    /// <summary>
    /// Two modules on one instrument share the register, so they cannot disagree
    /// about what it is doing — the same bargain two Samples on one clip make.
    /// </summary>
    [Fact]
    public void Two_modules_listening_to_one_instrument_ask_for_it_once()
    {
        var builder = new PatchBuilder(NodeCatalog.BuiltIn);
        var first = builder.Add(NodeCatalog.MidiTypeId, 0, 0);
        var second = builder.Add(NodeCatalog.MidiTypeId, 0, 0);
        first.SetState(MidiExtra.StateKey, new System.Text.Json.Nodes.JsonObject
        {
            [MidiExtra.IndexField] = 1f,
        });
        second.SetState(MidiExtra.StateKey, new System.Text.Json.Nodes.JsonObject
        {
            [MidiExtra.IndexField] = 1f,
        });
        var sink = builder.Add(NodeCatalog.OutputTypeId, 0, 0, (NodeCatalog.OutputGainPort, 1f));

        builder.Wire(first, Pitch, sink, NodeCatalog.OutputLeftPort);
        builder.Wire(second, Pitch, sink, NodeCatalog.OutputRightPort);

        var program = builder.Patch.CompileForAudio(NodeCatalog.BuiltIn).Program;

        program.LiveInputs.Count(key => key.EndsWith(MidiSignal.Pitch)).ShouldBe(1);
        program.Ops.Count(op => op.Code == OpCode.LoadLive).ShouldBe(program.LiveInputs.Count);
    }

    /// <summary>
    /// An instrument that is not here is reported and kept, not quietly swapped
    /// for one that is. The patch goes on meaning the keyboard it was written
    /// for.
    /// </summary>
    [Fact]
    public void A_device_that_is_not_plugged_in_is_said_and_kept()
    {
        var result = Built(Pitch, "usb:some-keyboard").CompileForAudio(NodeCatalog.BuiltIn);

        result.Issues.ShouldContain(issue => issue.Message.Contains("usb:some-keyboard"));
        result.Program.LiveInputs.ShouldContain(
            MidiSignal.Key("usb:some-keyboard", MidiSignal.Pitch));
    }

    /// <summary>
    /// A fresh one is listening to something rather than to nothing, and what it
    /// is listening to survives being written down and read back.
    /// </summary>
    [Fact]
    public void Which_instrument_it_listens_to_is_in_the_file()
    {
        var patch = new Patch();
        var midi = NodeInstance.Create(NodeCatalog.BuiltIn.Require(NodeCatalog.MidiTypeId), 0, 0);

        patch.Nodes.Add(midi);

        midi.StateOf(MidiExtra.StateKey)?[MidiExtra.DeviceField]?.GetValue<string>()
            .ShouldBe(MidiSources.Keyboard);

        midi.SetState(MidiExtra.StateKey, new System.Text.Json.Nodes.JsonObject
        {
            [MidiExtra.DeviceField] = "usb:some-keyboard",
        });

        var reopened = PatchIo.Read(PatchIo.ToJson(patch, NodeCatalog.BuiltIn), NodeCatalog.BuiltIn);

        reopened.Patch.Nodes.Single(node => node.TypeId == NodeCatalog.MidiTypeId)
            .StateOf(MidiExtra.StateKey)?[MidiExtra.DeviceField]
            ?.GetValue<string>()
            .ShouldBe("usb:some-keyboard");
    }

    /// <summary>The computer's own keys are always somewhere to play from.</summary>
    [Fact]
    public void The_computer_keyboard_is_always_on_offer()
    {
        MidiSources.All.ShouldContain(source => source.Id == MidiSources.Keyboard);
    }
}
