using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// The four-channel mixer. Four inputs, a level on each and one output, which
/// is the module a desk of Multiplies and Adds used to be.
/// </summary>
/// <remarks>
/// Its sockets are untyped, so the interesting property is not the arithmetic
/// but that there is only one of it: the same module runs at both sinks, mixing
/// pictures where a color arrives and tones where a scalar does. Both are run
/// here through a whole patch rather than through the emit alone, because the
/// coercion that makes that work happens at the port and at the sink rather
/// than inside the module.
/// </remarks>
public class MixerTests
{
    private const string Mixer = "math.mixer";

    /// <summary>Socket index of channel <paramref name="channel"/>'s input, counting from one.</summary>
    private static int In(int channel) => (channel - 1) * 2;

    /// <summary>Socket index of channel <paramref name="channel"/>'s level.</summary>
    private static int Level(int channel) => (channel - 1) * 2 + 1;

    /// <summary>
    /// The mixer's own knobs, into the Output's left, read as one sample. Every
    /// channel is a constant, so what comes out is the weighted sum and nothing
    /// else.
    /// </summary>
    private static float Heard(params (int Port, float Value)[] knobs)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var mixer = b.Add(Mixer, 0, 0, knobs);
        var output = b.Add(NodeCatalog.OutputTypeId, 200, 0, (NodeCatalog.OutputGainPort, 1f));

        b.Wire(mixer, 0, output, NodeCatalog.OutputLeftPort);

        var result = b.Patch.CompileForAudio(NodeCatalog.BuiltIn);
        result.HasErrors.ShouldBeFalse();

        var registers = result.Program.AllocateRegisters();
        result.Program.Evaluate(0d, 0d, 0d, registers, default);

        return (float)registers[result.Program.OutputBase];
    }

    [Fact]
    public void Every_level_starts_open_so_a_freshly_placed_mixer_passes_what_it_is_given()
    {
        var def = NodeCatalog.BuiltIn.Require(Mixer);

        for (var channel = 1; channel <= 4; channel++)
        {
            def.Inputs[In(channel)].Default.ShouldBe(0f, $"in {channel}");
            def.Inputs[Level(channel)].Default.ShouldBe(1f, $"level {channel}");
        }

        def.Outputs.ShouldHaveSingleItem().Name.ShouldBe("out");
    }

    [Fact]
    public void Each_input_arrives_at_the_output_scaled_by_its_own_level()
    {
        Heard((In(1), 1f), (Level(1), 0.25f)).ShouldBe(0.25f, 1e-5f);
        Heard((In(2), 1f), (Level(2), 0.5f)).ShouldBe(0.5f, 1e-5f);
        Heard((In(3), 2f), (Level(3), 0.5f)).ShouldBe(1f, 1e-5f);
        Heard((In(4), 1f), (Level(4), 0f)).ShouldBe(0f, 1e-5f);
    }

    /// <summary>
    /// It sums rather than averages, the way a desk does — four things at full
    /// is four times as loud, and a channel pulled down does not make the rest
    /// louder.
    /// </summary>
    [Fact]
    public void The_channels_sum_rather_than_average()
    {
        Heard(
            (In(1), 1f), (In(2), 1f), (In(3), 1f), (In(4), 1f))
            .ShouldBe(4f, 1e-5f);

        Heard(
            (In(1), 1f), (In(2), 1f), (In(3), 1f), (In(4), 1f),
            (Level(3), 0f), (Level(4), 0f))
            .ShouldBe(2f, 1e-5f);
    }

    /// <summary>
    /// The whole of what makes it usable: an unused channel rests at zero, so a
    /// mixer with two things patched into it is a two-channel mixer and needs no
    /// setting up to become one.
    /// </summary>
    [Fact]
    public void An_unused_channel_adds_nothing_however_its_level_is_set()
    {
        Heard((In(1), 0.75f)).ShouldBe(0.75f, 1e-5f);
        Heard((In(1), 0.75f), (Level(2), 1f), (Level(3), 1f), (Level(4), 1f)).ShouldBe(0.75f, 1e-5f);
    }

    /// <summary>
    /// A level is a socket like every other one here, which is what makes a
    /// fader something a signal can sweep rather than only something a hand can
    /// set. Wired from a Time it rises with the clock.
    /// </summary>
    [Fact]
    public void A_level_can_be_patched_rather_than_only_turned()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var time = b.Add("time", 0, 0);
        var mixer = b.Add(Mixer, 200, 0, (In(1), 1f));
        var output = b.Add(NodeCatalog.OutputTypeId, 400, 0, (NodeCatalog.OutputGainPort, 1f));

        b.Wire(time, 0, mixer, Level(1))
         .Wire(mixer, 0, output, NodeCatalog.OutputLeftPort);

        var result = b.Patch.CompileForAudio(NodeCatalog.BuiltIn);
        var registers = result.Program.AllocateRegisters();

        result.Program.Evaluate(0d, 0d, 0.25d, registers, default);
        ((float)registers[result.Program.OutputBase]).ShouldBe(0.25f, 1e-5f);

        result.Program.Evaluate(0d, 0d, 0.75d, registers, default);
        ((float)registers[result.Program.OutputBase]).ShouldBe(0.75f, 1e-5f);
    }

    /// <summary>
    /// The same module at the other sink. A color patched into any input makes
    /// the mix a color, and a scalar on another channel broadcasts across all
    /// three the way a shading language would — so four pictures at four levels
    /// is the mixer doing exactly what it does for four tones.
    /// </summary>
    [Fact]
    public void A_color_on_any_input_makes_the_mix_a_color()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var red = b.Add("color.rgb", 0, 0, (0, 1f), (1, 0f), (2, 0f));
        var green = b.Add("color.rgb", 0, 200, (0, 0f), (1, 1f), (2, 0f));

        // Two pictures at half each, and a scalar third channel that lifts every
        // component together — the broadcast is the part worth pinning.
        var mixer = b.Add(
            Mixer, 200, 0,
            (Level(1), 0.5f), (Level(2), 0.5f), (In(3), 0.25f), (Level(3), 1f));

        var output = b.Add(NodeCatalog.OutputTypeId, 400, 0);

        b.Wire(red, 0, mixer, In(1))
         .Wire(green, 0, mixer, In(2))
         .Wire(mixer, 0, output, NodeCatalog.OutputColorPort);

        var result = b.Patch.CompileForVideo(NodeCatalog.BuiltIn);
        result.HasErrors.ShouldBeFalse();
        result.Program.OutputWidth.ShouldBe(NodeCatalog.VideoChannels);

        var registers = result.Program.AllocateRegisters();
        result.Program.Evaluate(0d, 0d, 0d, registers, default);

        var at = result.Program.OutputBase;
        ((float)registers[at + 0]).ShouldBe(0.75f, 1e-5f, "red at half, plus the scalar");
        ((float)registers[at + 1]).ShouldBe(0.75f, 1e-5f, "green at half, plus the scalar");
        ((float)registers[at + 2]).ShouldBe(0.25f, 1e-5f, "the scalar alone");
    }

    /// <summary>
    /// Convenience is the whole of the module, so it has to cost what wiring the
    /// same thing by hand costs: four multiplies and three adds, and no more.
    /// </summary>
    [Fact]
    public void It_costs_what_the_same_thing_wired_by_hand_costs()
    {
        var emitter = new Emitter();
        var def = NodeCatalog.BuiltIn.Require(Mixer);

        var inputs = new Slot[def.Inputs.Count];
        for (var port = 0; port < inputs.Length; port++)
            inputs[port] = emitter.Load(OpCode.LoadT);

        def.Emit(emitter, new EmitContext(inputs));

        var program = emitter.ToProgram();
        program.Count(o => o.Code == OpCode.Mul).ShouldBe(4);
        program.Count(o => o.Code == OpCode.Add).ShouldBe(3);
    }

    /// <summary>
    /// The preset that ships the module, at the sink where summing can actually
    /// hurt. Four voices through a mixer is four times a voice at worst, and the
    /// Output's gain is set to the quarter that answers it — so the chord lands
    /// at full scale in the worst case rather than past it, and nobody has to
    /// hear what past it sounds like.
    /// </summary>
    /// <remarks>
    /// Twelve seconds because the faders are the slow part: the quickest is a
    /// sixth of a hertz, so anything shorter than several of its cycles never
    /// sees the four of them near the top together.
    /// </remarks>
    [Fact]
    public void The_preset_that_demonstrates_it_stays_inside_what_the_speakers_carry()
    {
        var patch = Presets.All.Single(p => p.Name == "Four voices").Build(NodeCatalog.BuiltIn);
        var program = patch.CompileForAudio(NodeCatalog.BuiltIn).Program;

        var buffer = new float[AudioRenderer.DefaultSampleRate * 12 * NodeCatalog.AudioChannels];
        new AudioRenderer().Render(program, buffer, AudioScan.TimeDriven);

        var peak = buffer.Max(Math.Abs);

        peak.ShouldBeGreaterThan(0.1f, "a chord of four voices ought to be audible");
        peak.ShouldBeLessThanOrEqualTo(1f, "four voices summed must not run past full scale");
    }
}
