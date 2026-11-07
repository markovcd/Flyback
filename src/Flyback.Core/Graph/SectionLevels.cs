using Flyback.Core.Compile;

namespace Flyback.Core.Graph;

/// <summary>
/// A part's level in the section playing, for the Arrangement and the Song: where it is
/// and where it came from, blended by how far the change between them has got.
/// </summary>
/// <remarks>
/// Both are chains of Mixes on section edges, so a part costs an op for each change of
/// level and nothing for a section that holds the one before's. A level is reached over
/// <paramref name="eased"/>, or over the whole section where it glides.
/// </remarks>
/// <param name="eased">How far a change has got, 0 to 1, over the fade.</param>
/// <param name="progress">How far through its section the part is, for a level that glides.</param>
/// <param name="fromInput">1 where section one still comes from the part's input rather than from its last section.</param>
internal sealed class SectionLevels(Emitter em, Slot eased, Slot progress, Slot fromInput)
{
    /// <summary>How far a part that holds one level throughout has got from its input to that level.</summary>
    private readonly Slot settled = em.Ternary(OpCode.Mix, em.Constant(1f), eased, fromInput);

    /// <summary>Whether the section playing is at or past each one, from the second: the same edges a sequence's windows use.</summary>
    public static Slot[] Edges(Emitter em, Slot index, int sections)
    {
        var edges = new Slot[sections];
        for (var s = 1; s < sections; s++) edges[s] = em.Binary(OpCode.Step, em.Constant(s), index);
        return edges;
    }

    /// <summary>The level of a part of <paramref name="levels"/>.Count sections, stepping on <paramref name="edges"/>.</summary>
    public Slot Part(IReadOnlyList<PartLevel> levels, Slot[] edges, Slot input)
    {
        var sections = levels.Count;

        // ReSharper disable once CompareOfFloatsByEqualityOperator
        if (levels.All(level => level.Value == levels[0].Value))
            return em.Ternary(OpCode.Mix, input, em.Constant(levels[0].Value), settled);

        var now = Chain(s => levels[s].Value);
        var was = em.Ternary(OpCode.Mix, Chain(s => levels[(s + sections - 1) % sections].Value), input, fromInput);

        var ramp = levels.Any(level => level.Glides)
            ? em.Ternary(OpCode.Mix, eased, progress, Chain(s => levels[s].Glides ? 1f : 0f))
            : eased;

        return em.Add(was, em.Mul(em.Sub(now, was), ramp));

        // The value for the section playing, a Mix at each edge where it changes.
        Slot Chain(Func<int, float> at)
        {
            var held = at(0);
            var value = em.Constant(held);

            for (var s = 1; s < sections; s++)
            {
                // ReSharper disable once CompareOfFloatsByEqualityOperator
                if (at(s) == held) continue;

                held = at(s);
                value = em.Ternary(OpCode.Mix, value, em.Constant(held), edges[s]);
            }

            return value;
        }
    }
}
