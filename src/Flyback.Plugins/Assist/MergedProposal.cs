using Flyback.Core.Graph;

namespace Flyback.Plugins.Assist;

/// <summary>
/// A proposal with the settings the person changed on the canvas while it was made carried
/// into it: what the assistant's column and a program driving the editor both put on the canvas.
/// </summary>
/// <param name="Patch">What goes on the canvas.</param>
/// <param name="Text">The text it is written in, or null where a carried setting is not in that text and the canvas writes its own.</param>
/// <param name="Overwrote">Whether the person changed modules or wires meanwhile, which the proposal replaces.</param>
/// <param name="Carried">Every setting the person changed meanwhile, and whether each was kept.</param>
internal sealed record MergedProposal(Patch Patch, string? Text, bool Overwrote, IReadOnlyList<Retuned> Carried)
{
    /// <summary>
    /// <paramref name="proposal"/>, made from <paramref name="was"/>, with what went from
    /// <paramref name="was"/> to <paramref name="now"/> on the canvas carried onto it.
    /// </summary>
    public static MergedProposal Of(Patch was, Patch now, Patch proposal, string? text)
    {
        if (!PatchShape.Of(was).Matches(now)) return new(proposal, text, Overwrote: true, []);

        var carried = Retuning.Carry(was, now, proposal);

        return new(proposal, carried.Any(change => change.Kept) ? null : text, Overwrote: false, carried);
    }

    /// <summary>How many settings the person changed meanwhile were kept.</summary>
    public int Kept => Carried.Count(change => change.Kept);

    /// <summary>How many settings the person changed meanwhile the proposal set too, whose value stands.</summary>
    public int Overruled => Carried.Count - Kept;
}
