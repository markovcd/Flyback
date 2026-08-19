using Flyback.Core.Compile;

namespace Flyback.Core.Graph;

/// <summary>
/// What one node is lowered from: its resolved inputs, and whatever the instance
/// carries that is not a knob.
/// </summary>
/// <remarks>
/// Indexes straight through to <see cref="Inputs"/>, so a module that wants
/// nothing but its sockets reads exactly as it always did — <c>i[0]</c> is input
/// zero. Only the sequencers look further, and only at <see cref="Steps"/>.
/// </remarks>
/// <param name="Inputs">
/// One slot per declared input, already resolved — either the upstream node's
/// result or a constant from the port default — and already coerced to the width
/// the port declared. A <see cref="PortSpec.Swept"/> input is the exception: it
/// holds its knob until <see cref="Resolve"/> is called for it.
/// </param>
/// <param name="Steps">
/// The instance's notes, empty for every module that has none. These are values
/// rather than slots on purpose: a sequencer folds its lengths into running sums
/// at compile time, which it could not do with a register.
/// </param>
public readonly record struct EmitContext(Slot[] Inputs, IReadOnlyList<Step> Steps)
{
    public Slot this[int port] => Inputs[port];

    /// <summary>
    /// Lowers whatever a <see cref="PortSpec.Swept"/> input is fed by, now
    /// rather than before the module was entered.
    /// </summary>
    /// <remarks>
    /// The whole point of the delay is what may have happened in between: a
    /// Probe pushes a domain of its own onto the emitter first, so everything
    /// upstream of the socket is lowered reading that instead of the pixel's own
    /// x, y and t. Nothing resolved here is shared with anything resolved
    /// outside the call, because a module read at one moment and the same module
    /// read at another are two different values.
    /// <para>
    /// Falls back to the port's knob when there is no resolver, so a module that
    /// calls this is still safe to emit outside a compilation.
    /// </para>
    /// </remarks>
    public Slot Resolve(int port) => Resolver is null ? Inputs[port] : Resolver(port);

    /// <summary>How the compiler resolves a deferred input, supplied by it.</summary>
    public Func<int, Slot>? Resolver { get; init; }
}

/// <summary>Lowers one node to register-machine ops.</summary>
public delegate Slot[] EmitFn(Emitter emitter, EmitContext node);

/// <summary>
/// The static description of a node type: its sockets and how it compiles.
/// Adding a new module to the synth means adding one of these to
/// <see cref="NodeCatalog"/> — nothing else in the pipeline needs to change.
/// </summary>
public sealed record NodeDef(
    string TypeId,
    string Name,
    string Category,
    IReadOnlyList<PortSpec> Inputs,
    IReadOnlyList<PortSpec> Outputs,
    EmitFn Emit,
    string Description = "")
{
    /// <summary>
    /// The notes a freshly placed instance carries, or null for a module that
    /// holds none. An init property rather than a constructor parameter, so a
    /// plugin compiled against an earlier build still finds the constructor it
    /// was compiled against.
    /// </summary>
    public IReadOnlyList<Step>? DefaultSteps { get; init; }

    /// <summary>
    /// Whether a wire may run backwards into this module — whether, in other
    /// words, a patch may hold a cycle that passes through it.
    /// </summary>
    /// <remarks>
    /// A module that says yes is not compiled the way every other module is. The
    /// walk stops when it reaches one and hands back what the cycle was carrying
    /// at the end of the previous evaluation, and the module's own input is
    /// resolved afterwards, once every such read in the program has been emitted.
    /// So <see cref="Emit"/> is never called on it — see
    /// <c>PatchCompiler</c> — and the latency that makes the loop mean something
    /// comes from that ordering rather than from anything the module does.
    /// <para>
    /// One input and one output, both scalar. Nothing enforces that, but a breaker
    /// with a different shape has sockets the compiler will not look at.
    /// </para>
    /// </remarks>
    public bool IsCycleBreaker { get; init; }

    /// <summary>How a step's value should be written out, when this module has steps.</summary>
    public PortDisplay StepDisplay { get; init; }

    /// <summary>The range a step's value is edited within, when this module has steps.</summary>
    public (float Min, float Max) StepRange { get; init; } = (0f, 1f);

    /// <summary>
    /// A step's value described as though it were a socket, so the editor formats
    /// and snaps it with the same code every knob already uses — a note in a list
    /// reads "A3" for the same reason a note on a knob does.
    /// </summary>
    public PortSpec StepValue => new(
        "value", PortKind.Scalar, 0f, StepRange.Min, StepRange.Max, -1, StepDisplay);
}
