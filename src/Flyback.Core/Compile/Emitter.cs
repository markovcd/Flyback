namespace Flyback.Core.Compile;

/// <summary>
/// Builds the flat op list for a patch. Every value gets a freshly allocated
/// register, so the program is in SSA form and no op ever aliases its inputs.
/// </summary>
public sealed class Emitter
{
    private readonly List<Op> ops = [];
    private readonly Dictionary<float, Slot> constants = [];
    private readonly Dictionary<OpCode, Slot> loads = [];

    /// <summary>The register each live input was read into, so it is read once.</summary>
    private readonly Dictionary<string, Slot> lives = [];

    /// <summary>The clips this program reads, one entry per distinct one.</summary>
    private readonly List<LoadedSample> tables = [];

    /// <summary>The pictures this program reads, one entry per distinct one.</summary>
    private readonly List<LoadedImage> pictures = [];

    /// <summary>
    /// The live inputs this program reads, one entry per distinct one — see
    /// <see cref="Live"/>.
    /// </summary>
    private readonly List<string> liveInputs = [];

    /// <summary>
    /// The (x, y, t) that <see cref="Load"/> hands back in place of the pixel's
    /// own, while something is being read over a domain the patch supplies.
    /// </summary>
    private readonly Stack<(Slot X, Slot Y, Slot T)> domains = new();

    /// <summary>
    /// The two cells every stateful module wants and none of them owns — see
    /// <see cref="Interval"/> and <see cref="HasMemory"/>. Held here rather than
    /// asked for twice, so a patch with four filters in it costs two cells and
    /// not eight.
    /// </summary>
    private Slot? interval;
    private Slot? memory;

    public int RegisterCount { get; private set; }

    /// <summary>
    /// How many one-evaluation cells have been handed out. Unlike a register these
    /// are not SSA — a cell is read and then written, which is the point of it —
    /// so they are counted separately and by hand.
    /// </summary>
    public int UnitSlotCount { get; private set; }

    /// <summary>Claims a cell for one cycle in the patch to carry a value round.</summary>
    public int AllocateUnitSlot() => UnitSlotCount++;

    public Op[] ToProgram() => [.. ops];

    private int Allocate(int count)
    {
        var first = RegisterCount;
        RegisterCount += count;
        return first;
    }

    private void Add(Op op) => ops.Add(op);

    /// <summary>A scalar literal. Repeated values share a register.</summary>
    public Slot Constant(float value)
    {
        if (constants.TryGetValue(value, out var existing)) return existing;

        var slot = Slot.Scalar(Allocate(1));
        Add(new Op(OpCode.Const, slot.Base, k: value));
        constants[value] = slot;
        return slot;
    }

    /// <summary>
    /// Reads what follows over a domain the patch supplies rather than the one
    /// the renderer does: every Coordinates and every Time resolved before the
    /// matching <see cref="PopDomain"/> is handed these three registers instead
    /// of the pixel's own.
    /// </summary>
    /// <remarks>
    /// This is how a Probe charts a signal. What it draws is the signal at a
    /// moment that varies along the picture, and in a program with one (x, y, t)
    /// per evaluation the only way to say that is to substitute the three where
    /// the subtree reads them. Nothing downstream can tell: what comes back are
    /// ordinary registers, so both backends run the result without knowing a
    /// domain was ever swapped.
    /// <para>
    /// A stack rather than three fields, because a Probe is an ordinary module
    /// and one may be read from inside another one's sweep.
    /// </para>
    /// </remarks>
    public void PushDomain(Slot x, Slot y, Slot t) => domains.Push((x, y, t));

    /// <summary>Puts back the domain that was in force before the matching push.</summary>
    public void PopDomain() => domains.Pop();

    /// <summary>A per-pixel input (x, y or time). Emitted once and reused.</summary>
    public Slot Load(OpCode code)
    {
        // A substituted domain neither reads the cache nor writes to it: these
        // registers belong to the sweep that pushed them, and the next sweep
        // will want different ones. Only the renderer's own x, y and t are
        // emitted once and shared by everything that asks for them.
        if (domains.Count > 0)
        {
            var (x, y, t) = domains.Peek();

            switch (code)
            {
                case OpCode.LoadX: return x;
                case OpCode.LoadY: return y;
                case OpCode.LoadT: return t;
            }
        }

        return Renderers(code);
    }

    /// <summary>The renderer's own input, whatever domain a sweep has pushed over it.</summary>
    private Slot Renderers(OpCode code)
    {
        if (loads.TryGetValue(code, out var existing)) return existing;

        var slot = Slot.Scalar(Allocate(1));
        Add(new Op(code, slot.Base));
        loads[code] = slot;
        return slot;
    }

    /// <summary>
    /// How far the renderer's clock moved since the previous evaluation, in
    /// seconds — which is the sample rate, said the other way round.
    /// </summary>
    /// <remarks>
    /// Nothing tells a module what rate it runs at, and this is how one finds
    /// out: a cell holds the clock as it was, and the difference is the interval.
    /// It is <see cref="Phase"/>'s trick written out — an oscillator advances by
    /// how far its domain moved (ADR-0030), and this measures how far time did —
    /// and it is what lets a filter turn a cutoff in hertz into a coefficient
    /// without the engine handing it anything.
    /// <para>
    /// Emitted once and shared by everything that asks, because what it measures
    /// is a property of the evaluation rather than of the module asking: two
    /// filters in one patch are stepping at the same rate by definition. A
    /// pushed domain does not change it for the same reason — a Probe sweeping
    /// time across the picture is drawing, and the clock it is drawn against is
    /// still the renderer's.
    /// </para>
    /// <para>
    /// Zero on the first evaluation, since there is no previous one to have
    /// moved from, and meaningless where the program has no state at all — see
    /// <see cref="HasMemory"/>, which is how a module says what it means there.
    /// </para>
    /// </remarks>
    public Slot Interval()
    {
        if (interval is { } existing) return existing;

        var now = Renderers(OpCode.LoadT);
        var cell = AllocateUnitSlot();
        var moved = Binary(OpCode.Sub, now, UnitRead(cell));

        // Written as a clock rather than as a signal, which is the difference
        // between a cell that is bounded to the rails and one that is not. What
        // goes in here is the time itself, and the time passes sixteen after
        // sixteen seconds — see OpCode.ClockWrite.
        ClockWrite(cell, now);

        return (interval = moved).Value;
    }

    /// <summary>
    /// One where the program has a memory behind it, zero where it has none.
    /// </summary>
    /// <remarks>
    /// A cell written one and read back as one from the second evaluation
    /// onwards — and read as zero for ever on the video path, where the renderer
    /// passes no state and a write goes nowhere. It is the only way for a module
    /// to ask the question at all: an emit function runs once, at compile time,
    /// long before anything knows which sink is about to run the program.
    /// <para>
    /// What it is for is choosing what a stateful module means where there is
    /// nothing to remember. Mixing on it gives a picture a decided answer —
    /// ADR-0041 — instead of whatever the arithmetic happens to fall out at.
    /// Shared like <see cref="Interval"/>, and for the same reason: the question
    /// has one answer per program.
    /// </para>
    /// </remarks>
    public Slot HasMemory()
    {
        if (memory is { } existing) return existing;

        var cell = AllocateUnitSlot();
        var live = UnitRead(cell);

        UnitWrite(cell, Constant(1f));

        return (memory = live).Value;
    }

    public Slot Unary(OpCode code, Slot a)
    {
        var first = Allocate(a.Width);
        for (var i = 0; i < a.Width; i++)
            Add(new Op(code, first + i, a.Component(i)));
        return new Slot(first, a.Width);
    }

    public Slot Binary(OpCode code, Slot a, Slot b)
    {
        var width = Math.Max(a.Width, b.Width);
        var first = Allocate(width);
        for (var i = 0; i < width; i++)
            Add(new Op(code, first + i, a.Component(i), b.Component(i)));
        return new Slot(first, width);
    }

    public Slot Ternary(OpCode code, Slot a, Slot b, Slot c)
    {
        var width = Math.Max(a.Width, Math.Max(b.Width, c.Width));
        var first = Allocate(width);
        for (var i = 0; i < width; i++)
            Add(new Op(code, first + i, a.Component(i), b.Component(i), c.Component(i)));
        return new Slot(first, width);
    }

    /// <summary>
    /// An op that owns a delay line. Scalar only, whatever arrives: a color
    /// would need three buffers, and there is nothing a delayed picture would
    /// mean that <see cref="OpCode.SampleFeedback"/> does not already do better.
    /// </summary>
    /// <param name="position"></param>
    /// <param name="clip"></param>
    /// <summary>
    /// Reads a loaded clip at a position in seconds. Two modules given the same
    /// clip share one table, the way two of them given the same number share one
    /// literal.
    /// </summary>
    public Slot Table(Slot position, LoadedSample clip)
    {
        var index = tables.IndexOf(clip);

        if (index < 0)
        {
            index = tables.Count;
            tables.Add(clip);
        }

        var first = Allocate(1);
        Add(new Op(OpCode.Table, first, position.Component(0), k: index));

        return Slot.Scalar(first);
    }

    /// <summary>The clips this program reads, in the order their ops name them.</summary>
    public IReadOnlyList<LoadedSample> Tables => tables;

    /// <summary>
    /// Reads a loaded picture at a place. Two modules given the same picture
    /// share one texture, the way two given the same clip share one table — which
    /// matters more here, because the sharing is what stops a patch showing one
    /// file four times from uploading it four times.
    /// </summary>
    public Slot Picture(Slot x, Slot y, LoadedImage image)
    {
        var index = pictures.IndexOf(image);

        if (index < 0)
        {
            index = pictures.Count;
            pictures.Add(image);
        }

        var first = Allocate(3);
        Add(new Op(OpCode.SamplePicture, first, x.Component(0), y.Component(0), k: index));

        return Slot.Color(first);
    }

    /// <summary>The pictures this program reads, in the order their ops name them.</summary>
    public IReadOnlyList<LoadedImage> Pictures => pictures;

    /// <summary>
    /// Reads whatever is being played into <paramref name="key"/> right now — a
    /// value that comes from outside the patch and may differ on the very next
    /// evaluation.
    /// </summary>
    /// <remarks>
    /// Two modules asking for the same key share one register, the way two given
    /// the same clip share one table and two given the same number share one
    /// literal. That is not merely a saving: two MIDI modules reading one
    /// keyboard have to agree about what it is doing, and sharing the register is
    /// how they cannot fail to.
    /// <para>
    /// Read once, where the walk first asks, and every later ask hands back that
    /// same register — so a key read by two modules reads the same in both,
    /// whatever order the walk reached them in. That is the promise
    /// <see cref="Load"/> makes about the clock, and it matters here for a
    /// sharper reason: one evaluation is one moment, and a key that was down for
    /// half of it was not down twice.
    /// </para>
    /// </remarks>
    public Slot Live(string key)
    {
        var index = liveInputs.IndexOf(key);

        if (index < 0)
        {
            index = liveInputs.Count;
            liveInputs.Add(key);
        }

        if (lives.TryGetValue(key, out var existing)) return existing;

        var slot = Slot.Scalar(Allocate(1));
        Add(new Op(OpCode.LoadLive, slot.Base, k: index));

        return lives[key] = slot;
    }

    /// <summary>What this program is played with, in the order its ops name them.</summary>
    public IReadOnlyList<string> LiveInputs => liveInputs;

    /// <summary>
    /// Keeps <paramref name="value"/> where something outside the program can
    /// look at it, and returns nothing because there is nothing to return.
    /// </summary>
    /// <param name="scope">
    /// Which trace, counted the same way in both of a patch's programs so that
    /// the one writing and the one drawing agree about which is which.
    /// </param>
    /// <param name="value"></param>
    public void Tap(int scope, Slot value) =>
        Add(new Op(OpCode.Tap, -1, value.Component(0), k: scope));

    public Slot DelayLine(OpCode code, Slot input, Slot gain, Slot time, float maximum)
    {
        var first = Allocate(1);
        Add(new Op(code, first, input.Component(0), gain.Component(0), time.Component(0), maximum));
        return Slot.Scalar(first);
    }

    /// <summary>
    /// A phase accumulator, carrying its running total from one evaluation to
    /// the next. Scalar only, like the delay lines and for the same reason: a
    /// color has no phase, and nothing would read one.
    /// </summary>
    /// <param name="input">
    /// The domain the oscillator runs over — Time, usually, but anything at all.
    /// Only how far it moves is used, which is what lets a patch keep driving an
    /// oscillator with a signal rather than a clock.
    /// </param>
    /// <param name="frequency">Cycles per unit of <paramref name="input"/>.</param>
    /// <param name="offset">
    /// Added after the accumulation rather than into it, so a phase input stays
    /// the direct offset it reads as and modulating it is still modulation.
    /// </param>
    public Slot Phase(Slot input, Slot frequency, Slot offset)
    {
        var first = Allocate(1);
        Add(new Op(
            OpCode.Phase,
            first,
            input.Component(0),
            frequency.Component(0),
            offset.Component(0)));
        return Slot.Scalar(first);
    }

    /// <summary>
    /// Reads what cycle <paramref name="slot"/> was carrying when the previous
    /// evaluation ended. Emitted where the graph asks for the value; the
    /// <see cref="UnitWrite"/> that fills the cell comes later, which is what puts
    /// an evaluation between the two.
    /// </summary>
    public Slot UnitRead(int slot)
    {
        var first = Allocate(1);
        Add(new Op(OpCode.UnitRead, first, k: slot));
        return Slot.Scalar(first);
    }

    /// <summary>
    /// Hands a value to cycle <paramref name="slot"/> for the next evaluation.
    /// Returns nothing, because there is no register to return: what this writes
    /// cannot be read again until the program runs anew.
    /// </summary>
    public void UnitWrite(int slot, Slot value) =>
        Add(new Op(OpCode.UnitWrite, -1, value.Component(0), k: slot));

    /// <summary>
    /// The same, for a cell holding a reading of a domain rather than a signal —
    /// see <see cref="OpCode.ClockWrite"/> for why the two are different ops.
    /// </summary>
    /// <remarks>
    /// The difference is the bound. A signal is clamped to the rails on its way
    /// into a cell, because a cycle drawn as wires has no coefficient to tame it
    /// and pinning a runaway is the only way to catch one. A domain reading is
    /// not a signal and that bound would be nonsense on it: the clock passes
    /// sixteen after sixteen seconds, and a cell that stopped there would take a
    /// module with it.
    /// <para>
    /// Public because a module may legitimately want to remember where a domain
    /// had got to — a Sample keeps the position its last trigger arrived at, and
    /// that is a clock reading in everything but name. Reach for
    /// <see cref="UnitWrite"/> for anything that is a signal.
    /// </para>
    /// </remarks>
    public void ClockWrite(int slot, Slot value) =>
        Add(new Op(OpCode.ClockWrite, -1, value.Component(0), k: slot));

    /// <summary>An op that writes three consecutive registers at once.</summary>
    public Slot Triple(OpCode code, Slot a, Slot b, Slot c = default)
    {
        var first = Allocate(3);
        Add(new Op(code, first, a.Component(0), b.Component(0), c.Width == 0 ? -1 : c.Component(0)));
        return Slot.Color(first);
    }

    // --- convenience wrappers used all over the node catalogue ---

    public Slot Add(Slot a, Slot b) => Binary(OpCode.Add, a, b);

    public Slot Sub(Slot a, Slot b) => Binary(OpCode.Sub, a, b);

    public Slot Mul(Slot a, Slot b) => Binary(OpCode.Mul, a, b);

    public Slot Mul(Slot a, float b) => Binary(OpCode.Mul, a, Constant(b));

    public Slot Add(Slot a, float b) => Binary(OpCode.Add, a, Constant(b));

    /// <summary>Widens a scalar to three components; colors pass through.</summary>
    private Slot ToColor(Slot value)
    {
        if (value.Width == 3) return value;

        var first = Allocate(3);
        for (var i = 0; i < 3; i++)
            Add(new Op(OpCode.Copy, first + i, value.Base));
        return Slot.Color(first);
    }

    /// <summary>Narrows a color to a scalar using broadcast luma weights.</summary>
    private Slot ToScalar(Slot value)
    {
        if (value.Width == 1) return value;

        // Luma is only defined for RGB; anything else averages what it has, so
        // this stays total no matter what width a future sink introduces.
        if (value.Width != 3)
        {
            var sum = Slot.Scalar(value.Base);
            for (var i = 1; i < value.Width; i++)
                sum = Add(sum, Slot.Scalar(value.Base + i));
            return Mul(sum, 1f / value.Width);
        }

        var r = Mul(Slot.Scalar(value.Base + 0), 0.2126f);
        var g = Mul(Slot.Scalar(value.Base + 1), 0.7152f);
        var b = Mul(Slot.Scalar(value.Base + 2), 0.0722f);
        return Add(Add(r, g), b);
    }

    /// <summary>
    /// Packs one slot per channel into a contiguous block of exactly
    /// <paramref name="width"/> registers — the shape every renderer reads.
    /// Missing channels are silence; a single color channel passes straight
    /// through, which is the video case.
    /// </summary>
    public Slot PackChannels(Slot[] channels, int width)
    {
        if (width == 3 && channels.Length == 1) return ToColor(channels[0]);

        // Resolve every source before allocating the block, so the copies are
        // emitted after the ops that produce what they read.
        var sources = new Slot[width];
        for (var i = 0; i < width; i++)
            sources[i] = i < channels.Length ? ToScalar(channels[i]) : Constant(0f);

        var first = Allocate(width);
        for (var i = 0; i < width; i++)
            Add(new Op(OpCode.Copy, first + i, sources[i].Base));

        return new Slot(first, width);
    }

    /// <summary>Packs three scalars into a color occupying consecutive registers.</summary>
    public Slot Combine(Slot r, Slot g, Slot b)
    {
        var first = Allocate(3);
        Add(new Op(OpCode.Copy, first + 0, r.Component(0)));
        Add(new Op(OpCode.Copy, first + 1, g.Component(0)));
        Add(new Op(OpCode.Copy, first + 2, b.Component(0)));
        return Slot.Color(first);
    }

    /// <summary>Coerces a value to the width a port expects.</summary>
    public Slot Coerce(Slot value, int width) => width == 3 ? ToColor(value) : ToScalar(value);
}
