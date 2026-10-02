namespace Flyback.Editor.Knobs;

/// <summary>Knobs on their way from where they were to where they are going, eased in and out.</summary>
internal sealed class KnobGlide(IReadOnlyDictionary<Guid, (float From, float To)> moves, TimeSpan length)
{
    private readonly Dictionary<Guid, (float From, float To)> moves = new(moves);

    /// <summary>The knobs still gliding.</summary>
    public IReadOnlyCollection<Guid> Knobs => moves.Keys;

    /// <summary>Where each gliding knob is <paramref name="elapsed"/> in.</summary>
    public IEnumerable<(Guid Id, float At)> At(TimeSpan elapsed)
    {
        var t = length <= TimeSpan.Zero ? 1 : (float)Math.Clamp(elapsed / length, 0, 1);
        var eased = t * t * (3 - 2 * t);

        return moves.Select(move => (move.Key, move.Value.From + (move.Value.To - move.Value.From) * eased));
    }

    /// <summary>Whether the glide has arrived <paramref name="elapsed"/> in.</summary>
    public bool Done(TimeSpan elapsed) => elapsed >= length || moves.Count == 0;

    /// <summary>Lets a knob go, for a hand that has taken it.</summary>
    public void Drop(Guid id) => moves.Remove(id);
}
