using Flyback.Core.Compile;

namespace Flyback.Core.Graph;

public partial class NodeCatalog
{
    private static IEnumerable<NodeDef> Oscillators()
    {
        yield return Oscillator("osc.sine", "Sine", (em, p) => em.Unary(OpCode.Sin, em.Mul(p, Tau)),
            "The basic waveform. Smooth bands and blobs.");

        yield return Oscillator("osc.saw", "Saw", (em, p) => em.Add(em.Mul(em.Unary(OpCode.Fract, p), 2f), -1f),
            "Ramps up then snaps back. Hard edges, good for stripes.");

        yield return Oscillator("osc.triangle", "Triangle",
            (em, p) => em.Add(em.Mul(em.Unary(OpCode.Abs, em.Add(em.Unary(OpCode.Fract, p), -0.5f)), 4f), -1f),
            "Linear up and down. Softer than saw, sharper than sine.");

        yield return Oscillator("osc.square", "Square",
            (em, p) => em.Add(em.Mul(em.Binary(OpCode.Step, em.Constant(0.5f), em.Unary(OpCode.Fract, p)), 2f), -1f),
            "Two values, nothing between. Pure hard-edged bands.");

        yield return new NodeDef(
            "osc.pulse", "Pulse", "Oscillator",
            [
                Domain("in"), Num("freq", 1f, 0f, 16f), Num("phase", 0f, 0f, 1f), Num("width", 0.5f, 0f, 1f),
                Num("amp", 1f, 0f, 2f), Num("bias", 0f, -2f, 2f)
            ],
            [Num("out")],
            (em, i) =>
            {
                var phase = em.Phase(i[0], i[1], i[2]);
                var wave = em.Add(em.Mul(em.Binary(OpCode.Step, i[3], em.Unary(OpCode.Fract, phase)), 2f), -1f);
                return [em.Add(em.Mul(wave, i[4]), i[5])];
            },
            "A square with an adjustable duty cycle.");
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
        id, name, "Oscillator",
        [
            Domain("in"),
            Num("freq", 1f, 0f, 16f),
            Num("phase", 0f, 0f, 1f),
            Num("amp", 1f, 0f, 2f),
            Num("bias", 0f, -2f, 2f),
        ],
        [Num("out")],
        (em, i) =>
        {
            var phase = em.Phase(i[0], i[1], i[2]);
            return [em.Add(em.Mul(waveform(em, phase), i[3]), i[4])];
        },
        description);
}