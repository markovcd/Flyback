using Flyback.Core.Compile;

namespace Flyback.Core.Graph;

/// <summary>
/// The static description of a node type: its sockets and how it compiles.
/// Adding a new module to the synth means adding one of these to
/// <see cref="NodeCatalog"/> — nothing else in the pipeline needs to change.
/// </summary>
/// <param name="Description">
/// What the module is for and how its sockets work together: the top of the
/// inspector, and the assistant's line about it. What one socket is for is that
/// socket's <see cref="PortSpec.Help"/>.
/// </param>
public sealed record NodeDef(
    string TypeId,
    string Name,
    string Category,
    IReadOnlyList<PortSpec> Inputs,
    IReadOnlyList<PortSpec> Outputs,
    EmitFn Emit,
    string Description = "")
{
    /// <summary>The inputs, with the standard help filled in on each that asks for it.</summary>
    public IReadOnlyList<PortSpec> Inputs { get; init => field = SocketHelp.Filled(value, input: true); } =
        SocketHelp.Filled(Inputs, input: true);

    /// <summary>The outputs, with the standard help filled in on each that asks for it.</summary>
    public IReadOnlyList<PortSpec> Outputs { get; init => field = SocketHelp.Filled(value, input: false); } =
        SocketHelp.Filled(Outputs, input: false);

    /// <summary>
    /// Everything an instance of this module carries that is not a knob: a
    /// sequencer's notes, a quantiser's scale, a player's file. Empty for the
    /// modules that are their sockets and nothing else.
    /// </summary>
    /// <remarks>
    /// A list of parts rather than a member or a subtype per kind (ADR-0054):
    /// what each kind does with a fresh, copied or compiled instance lives on the
    /// part, so a fourth kind adds a file. An init property, so a plugin compiled
    /// against an earlier build still finds the constructor it was compiled
    /// against.
    /// </remarks>
    public IReadOnlyList<NodeExtra> Extras { get; init; } = [];

    /// <summary>
    /// The extra of a given kind this module carries, or null where it carries
    /// none — so "has notes" is a question about the module rather than an
    /// observation that some default happens not to be null.
    /// </summary>
    public T? Extra<T>() where T : NodeExtra => Extras.OfType<T>().FirstOrDefault();

    /// <summary>
    /// Which input an instance that is switched off hands on at
    /// <paramref name="output"/>, or -1 for a module with no inputs to hand on.
    /// </summary>
    /// <remarks>
    /// The socket called <c>in</c>, which is the signal input wherever it sits
    /// in the list; failing that the one the output is named after, so a
    /// geometry module off hands its <c>x</c> to its <c>x</c>; failing that the
    /// first, the catalog being written with the principal socket at the top.
    /// The same ladder the language's pipe rule climbs, for the same reason: it
    /// is which socket a signal passing through this module travels on.
    /// <para>
    /// Only a wire on that socket is handed on. A normal is not — every
    /// oscillator's <c>in</c> is normalled to Time, and a voice switched off has
    /// to fall silent rather than pass a ramp down the patch.
    /// </para>
    /// </remarks>
    public int Through(int output)
    {
        if (Inputs.Count == 0) return -1;

        for (var port = 0; port < Inputs.Count; port++)
            if (string.Equals(Inputs[port].Name, "in", StringComparison.OrdinalIgnoreCase))
                return port;

        if (output >= 0 && output < Outputs.Count)
            for (var port = 0; port < Inputs.Count; port++)
                if (string.Equals(Inputs[port].Name, Outputs[output].Name, StringComparison.OrdinalIgnoreCase))
                    return port;

        return 0;
    }

    /// <summary>
    /// What this module is painted, or null to take its category's accent —
    /// see <see cref="ModuleSkin"/>.
    /// </summary>
    public ModuleSkin? Skin { get; init; }

    /// <summary>
    /// Whether an instance of this module watches what the speakers played — in
    /// other words, whether its first input is a root of the audio program as
    /// well as a socket.
    /// </summary>
    /// <remarks>
    /// The one flag that changes what the compiler walks rather than what it
    /// hands a module: a node nothing depends on must be visited anyway, because
    /// the whole of its use is a side effect — see <see cref="OpCode.Tap"/>.
    /// Declared rather than assumed, so a plugin can want it too.
    /// </remarks>
    public bool TapsSignal { get; init; }

    /// <summary>
    /// Whether the screen reads that stretch of the past back as a picture of
    /// itself — whether this module is a chart rather than a measurement.
    /// </summary>
    /// <remarks>
    /// The second half of <see cref="TapsSignal"/>, separate because the two are
    /// not the same size. A chart wants the whole window: a buffer per instance,
    /// refilled every frame and read as a table, which the shader takes as a texture.
    /// A measurement wants a number filled
    /// in from outside, and costs the picture nothing. Both still tap.
    /// </remarks>
    public bool ChartsSignal { get; init; }

    /// <summary>
    /// Whether what the chart holds is the frequency content of that stretch
    /// rather than the stretch itself — whether this module is an analyzer
    /// rather than a scope.
    /// </summary>
    /// <remarks>
    /// Means nothing without <see cref="ChartsSignal"/>: it changes what fills the
    /// buffer, not whether there is one. The buffer then runs from
    /// <see cref="Compile.SpectrumAxis.Lowest"/> to <see cref="Compile.SpectrumAxis.Highest"/>
    /// on a log axis and holds linear amplitude — see
    /// <c>Compile.Spectra</c>.
    /// </remarks>
    public bool ChartsSpectrum { get; init; }

    /// <summary>
    /// Whether the chart draws its first two inputs against each other, across and
    /// up, as the trace an X-Y oscilloscope's beam leaves: whether this module is a
    /// Beam.
    /// </summary>
    /// <remarks>
    /// Means nothing without <see cref="ChartsSignal"/>, and taps
    /// <see cref="TappedInputs"/> of two. The buffer holds the phosphor as a square
    /// picture rather than a stretch of time — see <c>Compile.Beams</c>.
    /// </remarks>
    internal bool ChartsBeam { get; init; }

    /// <summary>
    /// How many of the first inputs <see cref="TapsSignal"/> makes roots of the
    /// speakers' program, each a ring of its own.
    /// </summary>
    public int TappedInputs { get; init; } = 1;

    /// <summary>
    /// Which sink this module means something at. An init property defaulting to
    /// <see cref="ModuleSinks.Both"/>, so a plugin compiled against an earlier
    /// build neither has to say nor can be wrong.
    /// </summary>
    public ModuleSinks Sinks { get; init; } = ModuleSinks.Both;

    /// <summary>
    /// Whether this module lowers its inputs itself, each at the moment it reaches
    /// one, rather than being handed them all before it is entered.
    /// </summary>
    /// <remarks>
    /// Under the walk's own cache, unlike a <see cref="PortSpec.Swept"/> input: a
    /// signal read here and elsewhere is one register. What changes is only when
    /// it is lowered, which is what lets an Expression emit its ops in the order
    /// the modules it names would have — each operand where the formula arrives
    /// at it — and a socket it never reads is never lowered at all. Read through
    /// <see cref="EmitContext.Resolve"/>; what <see cref="EmitContext.Inputs"/>
    /// holds for one of these is nothing.
    /// </remarks>
    internal bool AsksForItsInputs { get; init; }

    /// <summary>
    /// How many voices a placed module of this kind starts a polyphonic wire with,
    /// or null for a module that starts none — see <see cref="VoiceCounts"/>.
    /// </summary>
    internal Func<NodeInstance, int>? StartsVoices { get; init; }

    /// <summary>
    /// Whether a polyphonic wire arriving here is summed to one voice in front of
    /// the module, rather than the module being lowered once per voice.
    /// </summary>
    /// <remarks>
    /// For what has to hear every voice at once: a sink, a chart, a meter, and a
    /// reverb whose memory is too large to keep once per voice.
    /// </remarks>
    internal bool MergesVoices { get; init; }
}