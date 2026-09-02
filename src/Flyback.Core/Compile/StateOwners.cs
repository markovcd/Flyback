namespace Flyback.Core.Compile;

/// <summary>
/// Which node owns each cell of a program's memory.
/// </summary>
/// <remarks>
/// <para>
/// A cell's index is its position among the ops of its kind
/// (<see cref="DelayState"/> says why), and a position is not an identity. Add
/// one oscillator upstream and every accumulator after it belongs to a different
/// module than it did — so a renderer with only the counts to go on can either
/// throw all of it away or hand an oscillator a stranger's phase. It threw it
/// away, which on a knob turn is invisible and on an edited patch is every tone
/// in it restarting at once.
/// </para>
/// <para>
/// This is the missing half: the compiler already knows whose op it is emitting,
/// and writing that down beside the counts turns "the shape changed" into "these
/// three cells are new and the rest carried on".
/// </para>
/// <para>
/// One node may own several cells of a kind — a supersaw is seven accumulators
/// under one handle — so a cell is matched to the cell in the same position
/// <em>within its owner</em>, which is stable however the program around it
/// moves.
/// </para>
/// </remarks>
/// <param name="Delays">Owner of each ring buffer, in program order.</param>
/// <param name="Phases">Owner of each accumulator, in program order.</param>
/// <param name="Units">Owner of each one-evaluation cell, by slot number.</param>
public sealed record StateOwners(
    IReadOnlyList<Guid> Delays,
    IReadOnlyList<Guid> Phases,
    IReadOnlyList<Guid> Units)
{
    /// <summary>
    /// What a program assembled by hand knows about itself, which is nothing.
    /// </summary>
    /// <remarks>
    /// Empty rather than absent, so every caller reads the same shape. A cell
    /// nobody claims is never adopted — see <see cref="Adopt"/> — which is the
    /// safe direction: a test that writes ops directly gets what it always got.
    /// </remarks>
    public static StateOwners None { get; } = new([], [], []);

    /// <summary>
    /// Nobody. A cell the compiler shares between modules rather than giving to
    /// one, and the two of those are the same two in every program that has
    /// them — see <see cref="Emitter.Interval"/> and
    /// <see cref="Emitter.HasMemory"/>.
    /// </summary>
    /// <remarks>
    /// A fixed name rather than the first module that happened to ask, which is
    /// what "shared" has to mean here: attributed to its first asker, the cell
    /// would be dropped the moment that module was deleted, and the interval it
    /// measures would read as a jump on the next evaluation.
    /// </remarks>
    public static Guid Shared { get; } = new("00000000-0000-0000-0000-0000000f1B00");

    /// <summary>
    /// Where each of <paramref name="mine"/> should read its previous value
    /// from, or -1 for a cell with no history to take.
    /// </summary>
    /// <remarks>
    /// <see cref="Guid.Empty"/> matches nothing, including itself. Not knowing
    /// who owns a cell is not the same as knowing two cells are the same cell,
    /// and the cost of being wrong here is a module playing something that was
    /// never its own.
    /// </remarks>
    public static int[] Adopt(IReadOnlyList<Guid> mine, IReadOnlyList<Guid> theirs)
    {
        var map = new int[mine.Count];

        Array.Fill(map, -1);

        if (theirs.Count == 0) return map;

        // Every cell each owner had, oldest first, so the nth cell of a module
        // takes over from the nth cell that module had before.
        var available = new Dictionary<Guid, Queue<int>>();

        for (var i = 0; i < theirs.Count; i++)
        {
            if (theirs[i] == Guid.Empty) continue;

            if (!available.TryGetValue(theirs[i], out var queue))
                available[theirs[i]] = queue = new Queue<int>();

            queue.Enqueue(i);
        }

        for (var i = 0; i < mine.Count; i++)
        {
            if (mine[i] == Guid.Empty) continue;
            if (!available.TryGetValue(mine[i], out var queue) || queue.Count == 0) continue;

            map[i] = queue.Dequeue();
        }

        return map;
    }

    /// <summary>Whether these name the same cells, in the same order, as <paramref name="other"/>.</summary>
    public bool Match(StateOwners other) =>
        Delays.SequenceEqual(other.Delays)
        && Phases.SequenceEqual(other.Phases)
        && Units.SequenceEqual(other.Units);
}
