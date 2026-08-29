using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// The preset that does nothing until somebody plays it, which is the one thing
/// no other preset test can check by rendering: every image and every buffer
/// approved elsewhere is a patch running on its own clock, and this one has no
/// clock of its own to run on.
/// </summary>
/// <remarks>
/// What is worth pinning is the shape of the wiring rather than the sound. That
/// the pulse widens when a note is struck is the Sample &amp; Hold's business and
/// is covered where that module is; that the trigger is one evaluation wide is
/// the MIDI In's and is covered there. Here it is that the three signals reach
/// the ear at all, that velocity does not, and that a key down is the difference
/// between silence and a note.
/// </remarks>
public class PlayedPresetTests
{
    private const int Rate = 48_000;

    private static Patch Patch() => Presets.All.Single(p => p.Name == "Played").Build(NodeCatalog.BuiltIn);

    private static string Key(string signal) => MidiSignal.Key(MidiSources.Keyboard, signal);

    /// <summary>
    /// Plays the preset for <paramref name="seconds"/> with one note either held
    /// or not, and hands back what the left channel did.
    /// </summary>
    private static float[] Play(double seconds, bool held, float pitch = 69f)
    {
        var compiled = Patch().CompileForAudio(NodeCatalog.BuiltIn);

        compiled.HasErrors.ShouldBeFalse(string.Join("; ", compiled.Issues.Select(i => i.Message)));

        var program = compiled.Program;
        var registers = program.AllocateRegisters();
        var state = new DelayState([], Rate, program.PhaseCount, program.UnitCount);
        var live = new LiveValues(program.LiveInputs);
        var samples = new float[(int)(seconds * Rate)];

        live.Set(Key(MidiSignal.Pitch), pitch);
        live.Set(Key(MidiSignal.Gate), held ? 1f : 0f);
        live.Set(Key(MidiSignal.Strikes), held ? 1f : 0f);

        for (var i = 0; i < samples.Length; i++)
        {
            program.Evaluate(0, 0, i / (double)Rate, registers, default, state, live: live);
            samples[i] = (float)registers[program.OutputBase];
        }

        return samples;
    }

    private static float Loudest(float[] samples) => samples.Max(MathF.Abs);

    /// <summary>
    /// A preset that ships must never need a plugin, and this one must never need
    /// a device either: the computer's keyboard is always there, so there is
    /// nothing for the compiler to complain about.
    /// </summary>
    [Fact]
    public void It_compiles_clean_at_both_sinks()
    {
        var patch = Patch();

        foreach (var compiled in new[]
                 {
                     patch.CompileForAudio(NodeCatalog.BuiltIn),
                     patch.CompileForVideo(NodeCatalog.BuiltIn),
                 })
        {
            compiled.Issues.ShouldBeEmpty(string.Join("; ", compiled.Issues.Select(i => i.Message)));
        }
    }

    /// <summary>
    /// Pitch, gate and the strike count all reach the speakers — the third by way
    /// of the trigger, which is the count differenced inside the program.
    /// </summary>
    [Fact]
    public void The_ear_reads_pitch_the_gate_and_what_the_trigger_is_made_of()
    {
        var heard = Patch().CompileForAudio(NodeCatalog.BuiltIn).Program.LiveInputs;

        heard.ShouldContain(Key(MidiSignal.Pitch));
        heard.ShouldContain(Key(MidiSignal.Gate));
        heard.ShouldContain(Key(MidiSignal.Strikes));
    }

    /// <summary>
    /// And nothing is wired to velocity, on purpose: a typist strikes every key
    /// the same, so a wire from it would be one that does nothing until hardware
    /// arrives.
    /// </summary>
    /// <remarks>
    /// Asked of the wires rather than of the program, because the program is not
    /// where the answer is. An emit function runs once and emits every output it
    /// has, whether or not anything downstream reads them — the same reason a
    /// Supersaw emits both of its channels into a patch using one — so the
    /// signal is named in <c>LiveInputs</c> either way and costs one register
    /// nothing reads. What the preset decides is which sockets carry a wire.
    /// </remarks>
    [Fact]
    public void Nothing_is_wired_to_velocity()
    {
        var patch = Patch();
        var keys = patch.FirstOf(NodeCatalog.MidiTypeId).ShouldNotBeNull();

        // 0 pitch, 1 gate, 2 velocity, 3 trigger.
        patch.Connections
            .Where(wire => wire.SourceNode == keys.Id)
            .Select(wire => wire.SourcePort)
            .Distinct()
            .Order()
            .ShouldBe([0, 1, 3]);
    }

    /// <summary>
    /// The eye reads which note and whether one is down, and neither of the two
    /// that need a past — a picture has none, so a trigger there is nought at
    /// every pixel and a Sample &amp; Hold holds nothing.
    /// </summary>
    [Fact]
    public void The_eye_reads_the_note_and_the_gate()
    {
        var drawn = Patch().CompileForVideo(NodeCatalog.BuiltIn).Program.LiveInputs;

        drawn.ShouldContain(Key(MidiSignal.Pitch));
        drawn.ShouldContain(Key(MidiSignal.Gate));
    }

    [Fact]
    public void Nothing_held_is_silence()
    {
        Loudest(Play(0.5, held: false)).ShouldBeLessThan(0.001f);
    }

    [Fact]
    public void A_key_held_is_a_note()
    {
        Loudest(Play(0.5, held: true)).ShouldBeGreaterThan(0.1f);
    }

    /// <summary>
    /// The note that comes out is the note that went in. Measured as a period
    /// rather than by looking at a register: what a patch is *for* is the sound,
    /// and A4 is 440 Hz by definition.
    /// </summary>
    [Theory]
    [InlineData(69f, 440f)]
    [InlineData(57f, 220f)]
    public void The_note_played_is_the_pitch_heard(float note, float hz)
    {
        // Past the attack, so the envelope is not still climbing through the
        // first crossings and moving them.
        var samples = Play(0.4, held: true, pitch: note).Skip(Rate / 10).ToArray();

        var crossings = 0;

        for (var i = 1; i < samples.Length; i++)
            if (samples[i - 1] <= 0f && samples[i] > 0f)
                crossings++;

        var measured = crossings / (samples.Length / (double)Rate);

        // Generous: the tone is a pulse through an envelope and a DC-free window
        // is not exactly a whole number of cycles.
        measured.ShouldBe(hz, hz * 0.05);
    }

    /// <summary>
    /// The envelope is what ends a note rather than the gate: letting go and then
    /// waiting has to fall to silence on its own.
    /// </summary>
    [Fact]
    public void Letting_go_lets_the_note_fall_away()
    {
        var compiled = Patch().CompileForAudio(NodeCatalog.BuiltIn);
        var program = compiled.Program;
        var registers = program.AllocateRegisters();
        var state = new DelayState([], Rate, program.PhaseCount, program.UnitCount);
        var live = new LiveValues(program.LiveInputs);

        live.Set(Key(MidiSignal.Pitch), 69f);
        live.Set(Key(MidiSignal.Strikes), 1f);
        live.Set(Key(MidiSignal.Gate), 1f);

        var loudest = 0f;
        var released = Rate / 4;
        var settled = released + Rate / 2;

        for (var i = 0; i < Rate; i++)
        {
            if (i == released) live.Set(Key(MidiSignal.Gate), 0f);

            program.Evaluate(0, 0, i / (double)Rate, registers, default, state, live: live);

            // Only what is heard after the release has had half a second to run.
            if (i > settled) loudest = MathF.Max(loudest, MathF.Abs((float)registers[program.OutputBase]));
        }

        loudest.ShouldBeLessThan(0.001f);
    }
}
