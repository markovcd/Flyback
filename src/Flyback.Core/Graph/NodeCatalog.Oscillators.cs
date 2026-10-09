using Flyback.Core.Compile;

namespace Flyback.Core.Graph;

public partial class NodeCatalog
{
    /// <summary>The type id of the Sine module.</summary>
    public const string SineTypeId = "osc.sine";
    /// <summary>The type id of the Saw module.</summary>
    internal const string SawTypeId = "osc.saw";
    /// <summary>The type id of the Triangle module.</summary>
    public const string TriangleTypeId = "osc.triangle";
    /// <summary>The type id of the Square module.</summary>
    internal const string SquareTypeId = "osc.square";
    /// <summary>The type id of the Pulse module.</summary>
    internal const string PulseTypeId = "osc.pulse";

    private static IEnumerable<NodeDef> Oscillators()
    {
        yield return Oscillator(SineTypeId, "Sine", (em, p) => em.Unary(OpCode.Sin, em.Mul(p, Tau)),
            "The basic waveform. Smooth bands and blobs.") with
        { Words = "smooth tone, bands, LFO" };

        yield return Oscillator(SawTypeId, "Saw", (em, p) => em.Add(em.Mul(em.Unary(OpCode.Fract, p), 2f), -1f),
            "Ramps up then snaps back. Hard edges, good for stripes.") with
        { Words = "buzzy tone, stripes" };

        yield return Oscillator(TriangleTypeId, "Triangle",
            (em, p) => em.Add(em.Mul(em.Unary(OpCode.Abs, em.Add(em.Unary(OpCode.Fract, p), -0.5f)), 4f), -1f),
            "Linear up and down. Softer than saw, sharper than sine.") with
        { Words = "soft tone" };

        yield return Oscillator(SquareTypeId, "Square",
            (em, p) => em.Add(em.Mul(em.Binary(OpCode.Step, em.Constant(0.5f), em.Unary(OpCode.Fract, p)), 2f), -1f),
            "Two values, nothing between. Pure hard-edged bands.") with
        { Words = "hollow tone, hard bands" };

        yield return new NodeDef(
            PulseTypeId, "Pulse", ModuleCategories.Oscillators,
            [
                Domain("in"), Freq, Phase, Num("width", 0.5f, 0f, 1f) with { Help = "Where in each cycle it flips from low to high: 0.5 is a square." },
                Amp, Bias,
            ],
            [Num("out", 0f, -1f, 1f) with { Help = Wave }],
            (em, i) =>
            {
                var phase = em.Phase(i[0], i[1], i[2]);
                var wave = em.Add(em.Mul(em.Binary(OpCode.Step, i[3], em.Unary(OpCode.Fract, phase)), 2f), -1f);
                return [em.Add(em.Mul(wave, i[4]), i[5])];
            },
            "A square with an adjustable duty cycle.")
        {
            Words = "square with duty cycle",
        };
    }

    /// <summary>
    /// Builds one of the fixed-shape oscillator modules. They share a socket
    /// layout and differ only in the waveform applied to the running phase.
    /// </summary>
    /// <remarks>
    /// The phase is accumulated rather than multiplied out, which is the whole
    /// of why a stepped pitch is silent on the audio path — see
    /// <see cref="OpCode.Phase"/>. Drawn rather than heard it is the multiply it
    /// always was, so the picture an oscillator makes is unchanged.
    /// </remarks>
    private static NodeDef Oscillator(
        string id, string name, Func<Emitter, Slot, Slot> waveform, string description) => new(
        id, name, ModuleCategories.Oscillators,
        [Domain("in"), Freq, Phase, Amp, Bias],
        [Num("out", 0f, -1f, 1f) with { Help = Wave }],
        (em, i) =>
        {
            var phase = em.Phase(i[0], i[1], i[2]);
            return [em.Add(em.Mul(waveform(em, phase), i[3]), i[4])];
        },
        description);

    private const string Wave = "The wave, after 'amp' and 'bias'.";

    private static PortSpec Phase => Num("phase", 0f, 0f, 1f) with { Lenient = true, Standard = true };

    private static PortSpec Amp => Num("amp", 1f, 0f, 2f) with { Standard = true };

    private static PortSpec Bias => Num("bias", 0f, -2f, 2f) with { Standard = true };
}
