using Flyback.Core.Compile;

namespace Flyback.Core.Graph;

/// <summary>
/// What one node is lowered from: its resolved inputs, and whatever the instance
/// carries that is not a knob.
/// </summary>
/// <remarks>
/// Indexes straight through to <see cref="Inputs"/>, so <c>i[0]</c> is input
/// zero. What is on it falls in three groups, laid out below in that order: the
/// sockets every module has; what the compiler knows and the module cannot — its
/// identity, and the buffer a Scope charts, both supplied where the context is
/// built rather than carried; and what the instance carries, each put there by a
/// <see cref="NodeExtra.Fold"/>.
/// <para>
/// Everything but <see cref="Inputs"/> is an init property defaulting to "none",
/// so an emit function may read any of it without asking, and a group gaining a
/// member is a property added — which keeps this constructor out of the plugin
/// ABI (ADR-0051).
/// </para>
/// </remarks>
/// <param name="Inputs">
/// One slot per declared input, already resolved and coerced to the width the
/// port declared. A <see cref="PortSpec.Swept"/> input is the exception: it
/// holds its knob until <see cref="Resolve"/> is called for it.
/// </param>
public readonly record struct EmitContext(Slot[] Inputs)
{
    public Slot this[int port] => Inputs[port];

    /// <summary>
    /// Lowers whatever a <see cref="PortSpec.Swept"/> input is fed by, now rather
    /// than before the module was entered.
    /// </summary>
    /// <remarks>
    /// The delay is for what may have happened in between: a Probe pushes a
    /// domain of its own first, so everything upstream is lowered reading that
    /// instead of the pixel's x, y and t, and nothing resolved here is shared
    /// with anything resolved outside the call. Falls back to the port's knob
    /// when there is no resolver, so a module that calls this is safe to emit
    /// outside a compilation.
    /// </remarks>
    public Slot Resolve(int port) => Resolver is null ? Inputs[port] : Resolver(port);

    /// <summary>How the compiler resolves a deferred input, supplied by it.</summary>
    public Func<int, Slot>? Resolver { get; init; }

    // --- what the compiler knows and the module cannot ---------------------------

    /// <summary>
    /// Which instance is being lowered, for the one kind of module that has to be
    /// addressable from outside the program.
    /// </summary>
    /// <remarks>
    /// Identity rather than state, which is why it is not down with
    /// <see cref="Sample"/>. A module needs it when its value is played into the
    /// program rather than computed — see <see cref="OpCode.LoadLive"/> — because
    /// the name it listens on must mean the same in both programs and to whatever
    /// fills it in, and a node id is the only thing all three agree on. Empty
    /// where a module is lowered without an instance behind it, which is the
    /// hidden one a normalled socket reads.
    /// </remarks>
    public Guid Node { get; init; }

    /// <summary>
    /// The buffer this instance charts — the stretch of the past something
    /// outside the program keeps refilling — and null on the program that plays
    /// rather than draws.
    /// </summary>
    /// <remarks>
    /// A <see cref="LoadedSample"/> like <see cref="Sample"/> and read the same
    /// way, so the module drawing it knows nothing about rings, threads or sound
    /// cards; what differs is that a clip never changes and this changes every
    /// frame. Supplied by the compiler where the context is built, the way
    /// <see cref="Node"/> is — there is no <c>TraceExtra</c> to look for, because
    /// a buffer is not in the patch file and cannot be seeded. Null on the
    /// speakers' program is the ordinary case: there a Scope contributes a
    /// <see cref="OpCode.Tap"/> the compiler emits without entering the module.
    /// </remarks>
    public LoadedSample? Trace { get; init; }

    // --- what the instance carries, each put here by a Fold ----------------------

    /// <summary>
    /// The instance's notes, empty for every module that has none. Values rather
    /// than slots: a sequencer folds its lengths into running sums at compile
    /// time, which a register could not do.
    /// </summary>
    public IReadOnlyList<Step> Steps
    {
        get => field ?? [];
        init;
    }

    /// <summary>
    /// The instance's scale — the notes of the octave a quantiser snaps to, and
    /// empty for every module that carries none.
    /// </summary>
    /// <remarks>
    /// Values rather than slots for the reason <see cref="Steps"/> is, and a
    /// stronger one: a quantiser emits one candidate per note switched on, so
    /// what is here decides how many ops the module lowers to rather than only
    /// what they compute. Empty rather than null, so an emit function may read it
    /// without asking.
    /// </remarks>
    public IReadOnlyList<int> Scale
    {
        get => field ?? [];
        init;
    }

    /// <summary>
    /// The clip this instance plays, already loaded, and null where there is none
    /// to play.
    /// </summary>
    /// <remarks>
    /// Null covers three things a module treats alike: no file chosen, the file
    /// gone, and the program being compiled is the screen's — a player with no
    /// clip is silence, which is the right answer to all three. Loaded rather
    /// than a path, because an emit function cannot read a file: this has been
    /// through <see cref="ISampleLibrary"/>, and the complaint about a missing
    /// one has already been made.
    /// </remarks>
    public LoadedSample? Sample { get; init; }

    /// <summary>
    /// The picture this instance shows, already read, and null where there is
    /// none to show.
    /// </summary>
    /// <remarks>
    /// Null covers the same three things <see cref="Sample"/>'s does, and the
    /// third is every audio program, since the compiler hands the speakers' walk
    /// no picture library. So a module needs no way to ask which sink it is being
    /// lowered for: the answer is whether it was given anything.
    /// </remarks>
    public LoadedImage? Picture { get; init; }

    /// <summary>
    /// What a plugin's own kinds of extra folded onto this context, keyed by
    /// <see cref="NodeExtra.Key"/>. Empty for every module in the engine's own
    /// catalogue, which read the typed properties above.
    /// </summary>
    /// <remarks>
    /// <c>object</c> because the engine does not know the shape: what goes in is
    /// put there by a plugin's <see cref="NodeExtra.Fold"/> and read by that same
    /// plugin's emit function, already parsed, so an emit function never sees the
    /// JSON. Read it with <see cref="Extra{T}"/> rather than by hand.
    /// </remarks>
    public IReadOnlyDictionary<string, object> Extras
    {
        get => field ?? EmptyExtras;
        init;
    }

    /// <summary>
    /// What the extra called <paramref name="key"/> folded on, or null where it
    /// folded nothing or folded something else. Null rather than a throw, so a
    /// module compiled against a catalogue that has moved under it lowers to
    /// something rather than taking the compilation down.
    /// </summary>
    public T? Extra<T>(string key) where T : class =>
        Extras.TryGetValue(key, out var value) ? value as T : null;

    /// <summary>
    /// The same context with one more extra folded on, which is what
    /// <see cref="NodeExtra.Fold"/> hands back.
    /// </summary>
    public EmitContext With(string key, object value) =>
        this with { Extras = new Dictionary<string, object>(Extras) { [key] = value } };

    /// <summary>Shared and empty, so a context that was never given any allocates nothing.</summary>
    private static readonly Dictionary<string, object> EmptyExtras = [];
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
    /// refilled every frame and read as a table, which the shader cannot draw, so
    /// a patch charting one draws on the CPU. A measurement wants a number filled
    /// in from outside, and costs the picture nothing. Both still tap.
    /// </remarks>
    public bool ChartsSignal { get; init; }

    /// <summary>
    /// Whether a wire may run backwards into this module — whether a patch may
    /// hold a cycle that passes through it.
    /// </summary>
    /// <remarks>
    /// The walk stops when it reaches one and hands back what the cycle carried
    /// at the end of the previous evaluation; the module's own input is resolved
    /// afterwards, once every such read has been emitted. So <see cref="Emit"/>
    /// is never called on it, and the latency that makes the loop mean something
    /// comes from that ordering. One input and one output, both scalar — nothing
    /// enforces it, but other sockets are sockets the compiler will not look at.
    /// </remarks>
    public bool IsCycleBreaker { get; init; }

    /// <summary>
    /// Which sink this module means something at. An init property defaulting to
    /// <see cref="ModuleSinks.Both"/>, so a plugin compiled against an earlier
    /// build neither has to say nor can be wrong.
    /// </summary>
    public ModuleSinks Sinks { get; init; } = ModuleSinks.Both;
}

/// <summary>
/// Which of the two programs evaluates a module as it is meant to be.
/// </summary>
/// <remarks>
/// Every module is compiled for both sinks — a patch is one graph (ADR-0022) —
/// so this changes nothing about what is emitted. It names what the catalogue
/// only ever said in prose: a Filter is a wire on the video path because it has
/// no memory to run in, a Meter reads nothing where no sound is running, and a
/// Quantiser's hold cannot hold across an evaluation the screen does not have.
/// Written down so the palette can badge it and the handbook can carry it. It is
/// not what decides which sink a program reaches: that falls out of the walk.
/// </remarks>
public enum ModuleSinks
{
    /// <summary>The same arithmetic at both, which is nearly everything.</summary>
    Both,

    /// <summary>
    /// Wants a memory, so the screen gets something simpler: a Filter is a wire
    /// there, an envelope hands over its gate, and a hold does not hold.
    /// </summary>
    Audio,

    /// <summary>
    /// Wants a pixel, a frame before this one, or a stretch of what the speakers
    /// played — so the speakers' own program never reads it.
    /// </summary>
    Video,
}
