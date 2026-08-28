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
/// <para>
/// What is on it falls in three groups, and they are laid out below in that
/// order because reading them as one is a mistake worth preventing. The sockets
/// come first and every module has them. Then what the compiler knows about this
/// node and the module cannot — its identity, and the buffer a Scope charts;
/// these are supplied where the context is built, and are not carried state
/// however much <see cref="Trace"/> looks like it. Last, what the instance
/// actually carries, every one of them put here by a
/// <see cref="NodeExtra.Fold"/>.
/// </para>
/// <para>
/// Everything but <see cref="Inputs"/> is an init property with a default that
/// means "none", so a context may be built for a module that wants none of it
/// and an emit function may read any of it without asking first. That is also
/// what keeps this type's constructor out of the plugin ABI
/// ([0051](0051-a-quantisers-scale-is-a-set-on-the-node.md)): a group gaining a
/// member is a property added, which a plugin compiled against an earlier build
/// does not notice.
/// </para>
/// </remarks>
/// <param name="Inputs">
/// One slot per declared input, already resolved — either the upstream node's
/// result or a constant from the port default — and already coerced to the width
/// the port declared. A <see cref="PortSpec.Swept"/> input is the exception: it
/// holds its knob until <see cref="Resolve"/> is called for it.
/// </param>
public readonly record struct EmitContext(Slot[] Inputs)
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

    // --- what the compiler knows and the module cannot ---------------------------

    /// <summary>
    /// Which instance is being lowered, for the one kind of module that has to
    /// be addressable from outside the program.
    /// </summary>
    /// <remarks>
    /// Identity rather than state, which is why it sits here rather than below
    /// with <see cref="Sample"/> and the rest: those are things an instance
    /// <em>carries</em> and this is only which instance it is. A module needs it
    /// when its value is not computed by the program at all but played into it —
    /// see <see cref="OpCode.LoadLive"/> — because the name it listens on has to
    /// mean the same thing in the screen's program, in the speakers', and to
    /// whatever outside is filling it in. A node id is the only thing all three
    /// can agree on: the two programs are compiled separately and share no
    /// numbering, exactly as <see cref="TapSpec.Node"/> found.
    /// <para>
    /// Empty where a module is lowered without an instance behind it — the hidden
    /// one a normalled socket reads. Such a module has no node to be addressed
    /// as, and a meter normalled to a socket would be measuring nothing anyway.
    /// </para>
    /// </remarks>
    public Guid Node { get; init; }

    /// <summary>
    /// The buffer this instance charts — the stretch of the past something
    /// outside the program keeps refilling — and null where the program being
    /// compiled is the one doing the playing rather than the drawing.
    /// </summary>
    /// <remarks>
    /// A <see cref="LoadedSample"/> like <see cref="Sample"/>, and read the same
    /// way, which is the point: a chart of what was played is a table read, and
    /// the module drawing it needs to know nothing about rings, threads or sound
    /// cards. What differs is who fills it — a clip arrives loaded and never
    /// changes, and this one changes every frame.
    /// <para>
    /// And who <em>puts</em> it here, which is why it sits in this group and not
    /// the next one. Nothing an instance carries says anything about a Scope: a
    /// buffer is not in the patch file, is not seeded, and cannot be. The
    /// compiler supplies it where the context is built, the way it supplies
    /// <see cref="Node"/> — so there is no <c>TraceExtra</c> to look for, and
    /// looking for one is the reading this grouping is here to prevent.
    /// </para>
    /// <para>
    /// Null on the speakers' program is not a fallback but the ordinary case:
    /// there the Scope is not drawn at all, and what it contributes is a
    /// <see cref="OpCode.Tap"/> the compiler emits without entering the module.
    /// </para>
    /// </remarks>
    public LoadedSample? Trace { get; init; }

    // --- what the instance carries, each put here by a Fold ----------------------

    /// <summary>
    /// The instance's notes, empty for every module that has none.
    /// </summary>
    /// <remarks>
    /// Values rather than slots on purpose: a sequencer folds its lengths into
    /// running sums at compile time, which it could not do with a register.
    /// <para>
    /// An init property rather than the constructor parameter it used to be. It
    /// was the first of these and predates the convention the other three were
    /// added under ([0051](0051-a-quantisers-scale-is-a-set-on-the-node.md)),
    /// which left it as the one piece of carried state in the plugin ABI — and
    /// left the compiler passing an empty list at both construction sites for
    /// <see cref="StepsExtra.Fold"/> to overwrite a moment later.
    /// </para>
    /// </remarks>
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
    /// stronger one. A quantiser emits one candidate per note in its scale and
    /// nothing at all for the notes left out, so what is here decides how many
    /// ops the module lowers to rather than only what they compute. A register
    /// could not do that: the shape of the program would have to cover all
    /// twelve however few were switched on.
    /// <para>
    /// The getter answers with an empty list rather than null so that an emit
    /// function may read it without asking, including on a context that was
    /// never given one — which is every module but the Quantiser.
    /// </para>
    /// </remarks>
    public IReadOnlyList<int> Scale
    {
        get => field ?? [];
        init;
    }

    /// <summary>
    /// The clip this instance plays, already loaded, and null where there is
    /// none to play.
    /// </summary>
    /// <remarks>
    /// Null covers three things a module treats the same way, which is why it is
    /// worth their sharing one: no file has been chosen, the file has gone, and
    /// the program being compiled is the screen's. A player with no clip is
    /// silence, and that is the right answer to all three.
    /// <para>
    /// Loaded rather than a path, because an emit function cannot read a file and
    /// must not want to. What is here has been through
    /// <see cref="ISampleLibrary"/> already, and the complaint about a file that
    /// was not there has already been made.
    /// </para>
    /// </remarks>
    public LoadedSample? Sample { get; init; }

    /// <summary>
    /// The picture this instance shows, already read, and null where there is
    /// none to show.
    /// </summary>
    /// <remarks>
    /// Null covers the same three things <see cref="Sample"/>'s does — no file
    /// chosen, a file that has gone, and a program that cannot show one — and
    /// the third is the interesting one: it is every audio program, because the
    /// compiler hands the speakers' walk no picture library at all. So a module
    /// reading this needs no way to ask which sink it is being lowered for; the
    /// answer is in whether it was given anything.
    /// </remarks>
    public LoadedImage? Picture { get; init; }

    /// <summary>
    /// What a plugin's own kinds of extra folded onto this context, keyed by
    /// <see cref="NodeExtra.Key"/>. Empty for every module in the engine's own
    /// catalogue, all of which read the typed properties above instead.
    /// </summary>
    /// <remarks>
    /// <c>object</c> because the engine does not know the shape and does not need
    /// to: what goes in here is put there by a plugin's own
    /// <see cref="NodeExtra.Fold"/> and read by that same plugin's emit function,
    /// and the pair agree about the type without anything between them having an
    /// opinion. Read it with <see cref="Extra{T}"/> rather than by hand.
    /// <para>
    /// Already parsed, so an emit function never sees the JSON: turning a stored
    /// tree into something usable — and tolerating one that means nothing —
    /// happened in <c>Fold</c>, before the module was entered.
    /// </para>
    /// </remarks>
    public IReadOnlyDictionary<string, object> Extras
    {
        get => field ?? EmptyExtras;
        init;
    }

    /// <summary>
    /// What the extra called <paramref name="key"/> folded on, or null where it
    /// folded nothing or folded something else.
    /// </summary>
    /// <remarks>
    /// Null rather than a throw on the wrong type, for the reason every other
    /// read here is forgiving: a module compiled with a stale patch, or against a
    /// catalogue that has moved under it, should lower to something rather than
    /// take the compilation down.
    /// </remarks>
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
    /// great majority of modules, which are their sockets and nothing else.
    /// </summary>
    /// <remarks>
    /// A list of parts rather than a member per kind, and rather than a subtype
    /// per kind ([0054](0054-what-a-module-carries-is-a-part-not-a-subtype.md)).
    /// What each kind does with a fresh instance, a copied one and a compiled
    /// one lives on the part — see <see cref="NodeExtra"/> — so adding a fourth
    /// kind adds a file rather than an edit to every place that used to name the
    /// three.
    /// <para>
    /// An init property rather than a constructor parameter, so a plugin
    /// compiled against an earlier build still finds the constructor it was
    /// compiled against.
    /// </para>
    /// </remarks>
    public IReadOnlyList<NodeExtra> Extras { get; init; } = [];

    /// <summary>
    /// The extra of a given kind this module carries, or null where it carries
    /// none. The one way to ask, so that "has notes" is a question about the
    /// module rather than an observation that some default happens not to be
    /// null.
    /// </summary>
    public T? Extra<T>() where T : NodeExtra => Extras.OfType<T>().FirstOrDefault();

    /// <summary>
    /// Whether an instance of this module watches what the speakers played —
    /// whether, in other words, its first input is a root of the audio program
    /// as well as a socket.
    /// </summary>
    /// <remarks>
    /// The one flag that changes what the compiler <em>walks</em> rather than
    /// what it hands a module. Everything else here is data a module reads; this
    /// says that a node nothing downstream depends on must be visited anyway,
    /// because the whole of its use is a side effect — see
    /// <see cref="OpCode.Tap"/>. Declared rather than assumed of the one module
    /// that wants it, so that a plugin can want it too.
    /// </remarks>
    public bool TapsSignal { get; init; }

    /// <summary>
    /// Whether the screen reads that stretch of the past back as a picture of
    /// itself — whether this module is a chart rather than a measurement.
    /// </summary>
    /// <remarks>
    /// The second half of <see cref="TapsSignal"/>, and separate from it because
    /// the two things a module can do with what the speakers played are not the
    /// same size. A chart wants the whole window, which is a buffer per instance,
    /// refilled every frame and read as a table — and a table is the one thing
    /// the shader cannot draw, so a patch charting one draws on the CPU. A
    /// measurement wants a number, which is filled in from outside like a note
    /// on a keyboard and costs the picture nothing at all.
    /// <para>
    /// So a module says which it is, and a module that only measures pays for
    /// neither the buffer nor the refill. Both still tap: the ring the speakers
    /// write is the same ring either way.
    /// </para>
    /// </remarks>
    public bool ChartsSignal { get; init; }

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
}
