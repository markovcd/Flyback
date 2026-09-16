namespace Flyback.Core.Compile;

/// <summary>Which of the two programs a patch compiles to a submission stands for.</summary>
public enum IlLane
{
    Picture,
    Sound,
}

/// <summary>
/// Puts IL under programs that are already playing: a program is interpreted the
/// moment it is compiled, and runs as machine code from whenever that is ready.
/// See ADR-0076.
/// </summary>
/// <remarks>
/// <para>
/// Nothing waits for this. <see cref="Submit"/> returns at once, and what it
/// submitted goes on being interpreted until a thread of this compiler's own has
/// built the IL and attached it with <see cref="CompiledPatch.Attach"/>. The
/// interpreter and the IL agree to the bit and share the program's memory, so
/// the moment one takes over from the other cannot be heard or seen.
/// </para>
/// <para>
/// Built code is kept by shape — the program with its constants left out — so a
/// knob turned by hand, which recompiles the patch on every move, finds code
/// already built and is bound to it on the spot. Only an edit that changes what
/// the program does rather than a number in it waits for the JIT, and only the
/// latest such edit per lane is built: one a later edit has replaced is dropped
/// unbuilt.
/// </para>
/// </remarks>
public sealed class IlCompiler : IDisposable
{
    /// <summary>
    /// How many shapes are kept. A live-coding session passes through a great
    /// many, and a knob sweep through a value another constant shares passes
    /// between two; a few dozen covers the second without keeping the first.
    /// </summary>
    private const int Capacity = 32;

    private static readonly int Lanes = Enum.GetValues<IlLane>().Length;

    private readonly Lock gate = new();
    private readonly Dictionary<IlKey, IlMethods> built = [];
    private readonly Queue<IlKey> age = new();
    private readonly HashSet<IlKey> refused = [];

    private readonly CompiledPatch?[] latest = new CompiledPatch?[Lanes];
    private readonly IlKey?[] latestKey = new IlKey?[Lanes];
    private readonly bool[] pending = new bool[Lanes];

    private readonly AutoResetEvent wake = new(false);
    private readonly Thread worker;

    private TaskCompletionSource settled = Completed();
    private bool enabled = true;
    private bool disposed;

    public IlCompiler()
    {
        // Below the audio callback and the render loop, which are what this is
        // trying to make cheaper: a build that took the processor from either of
        // them would cost the thing it exists to save.
        worker = new Thread(Work)
        {
            IsBackground = true,
            Name = "Flyback IL compiler",
            Priority = ThreadPriority.BelowNormal,
        };

        worker.Start();
    }

    /// <summary>
    /// Raised, on the compiler's thread, when a program could not be built or did
    /// not agree with the interpreter. That program goes on being interpreted, and
    /// a program of the same shape is not tried again.
    /// </summary>
    public event Action<string>? Failed;

    /// <summary>
    /// Whether programs run as IL at all. Turning it off takes the IL back off the
    /// programs submitted last and builds nothing more; turning it on submits them
    /// again.
    /// </summary>
    public bool Enabled
    {
        get
        {
            lock (gate) return enabled;
        }
        set
        {
            lock (gate)
            {
                if (enabled == value) return;
                enabled = value;

                for (var lane = 0; lane < Lanes; lane++)
                {
                    if (latest[lane] is not { } program) continue;

                    if (value) Enqueue(program, lane);
                    else
                    {
                        pending[lane] = false;
                        program.Attach(null);
                    }
                }

                if (!value) Settle();
            }
        }
    }

    /// <summary>
    /// Hands over the program now playing in <paramref name="lane"/>. Attached at
    /// once when code of its shape is already built, on the compiler's thread
    /// otherwise, and never once this compiler has been disposed.
    /// </summary>
    public void Submit(CompiledPatch program, IlLane lane)
    {
        ArgumentNullException.ThrowIfNull(program);

        lock (gate)
        {
            // Ignored rather than refused: a window closing can still have an edit
            // queued behind it, and that program plays interpreted, as it would have
            // until its IL arrived anyway.
            if (disposed) return;
            Enqueue(program, (int)lane);
        }
    }

    /// <summary>Completes once nothing is waiting to be built. For tests, and for anything that needs to know the swap has happened.</summary>
    public Task Settled()
    {
        lock (gate) return settled.Task;
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            Settle();
        }

        wake.Set();
    }

    /// <summary>
    /// What a lane's renderer calls, and so what is worth building for it: the
    /// speakers run the program whole and the screen runs it in stages, and neither
    /// calls the other. Building both would put a program through the JIT twice.
    /// </summary>
    private static IlParts PartsFor(int lane) => (IlLane)lane switch
    {
        IlLane.Sound => IlParts.Whole,
        _ => IlParts.Staged,
    };

    private void Enqueue(CompiledPatch program, int lane)
    {
        var key = new IlKey(new IlShape(program), PartsFor(lane));

        latest[lane] = program;
        latestKey[lane] = key;

        if (!enabled || refused.Contains(key)) return;

        if (built.TryGetValue(key, out var methods))
        {
            pending[lane] = false;
            if (program.Il?.Methods != methods) program.Attach(IlProgram.Bind(methods, program));
            return;
        }

        pending[lane] = true;
        if (settled.Task.IsCompleted) settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        wake.Set();
    }

    private void Work()
    {
        while (true)
        {
            wake.WaitOne();

            while (true)
            {
                CompiledPatch program;
                IlKey key;
                int lane;

                lock (gate)
                {
                    if (disposed) return;

                    lane = Array.IndexOf(pending, true);
                    if (lane < 0)
                    {
                        Settle();
                        break;
                    }

                    pending[lane] = false;
                    program = latest[lane]!;
                    key = latestKey[lane]!.Value;
                }

                var failure = Build(program, key.Parts, out var methods);

                lock (gate)
                {
                    if (disposed) return;

                    if (methods is null)
                    {
                        refused.Add(key);
                    }
                    else
                    {
                        Keep(key, methods);

                        // Whatever is playing now, which may be a later program
                        // than the one built — a knob turned while this ran.
                        if (enabled
                            && latest[lane] is { } now
                            && key.Equals(latestKey[lane])
                            && now.Il?.Methods != methods)
                            now.Attach(IlProgram.Bind(methods, now));
                    }
                }

                if (failure is not null) Failed?.Invoke(failure);
            }
        }
    }

    private static string? Build(CompiledPatch program, IlParts parts, out IlMethods? methods)
    {
        methods = null;

        try
        {
            var candidate = IlMethods.Build(program, parts);

            if (!IlProgram.Bind(candidate, program).AgreesWithInterpreter())
                return "The compiled program disagreed with the interpreter, so this patch stays interpreted.";

            methods = candidate;
            return null;
        }
        catch (Exception ex)
        {
            return $"This patch could not be compiled, so it stays interpreted — {ex.Message}";
        }
    }

    private void Keep(IlKey key, IlMethods methods)
    {
        if (!built.TryAdd(key, methods)) return;
        age.Enqueue(key);

        // Evicted code stays alive for as long as a program still holds it, and is
        // collected with that program: a dynamic method is garbage like anything else.
        while (age.Count > Capacity) built.Remove(age.Dequeue());
    }

    private void Settle() => settled.TrySetResult();

    private static TaskCompletionSource Completed()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }
}

/// <summary>What built code is kept under: the program's shape, and which of its parts were built.</summary>
internal readonly record struct IlKey(IlShape Shape, IlParts Parts);
