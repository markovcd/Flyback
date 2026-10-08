using Flyback.Core.Graph;

namespace Flyback.Plugins.Decide;

/// <summary>
/// How likely each of a patch's complaints is to be why it is silent or dark, so the
/// likeliest can be said first. Each complaint is the text one yes-no question is about.
/// </summary>
/// <remarks>
/// One request per complaint, with the complaint as the state and the question fixed. Asked
/// the other way round, as one question per complaint about the patch, the model rated "swings
/// past the range" above "plays silence" in every one of sixteen broken patches; this way it
/// put the cause first in all sixteen, on both checkpoints.
/// </remarks>
internal sealed class IssueTriage(Decisions decisions)
{
    /// <summary>What is asked of each complaint.</summary>
    public const string Question = "Does this complaint mean the patch is silent or shows nothing?";

    /// <summary>The most complaints asked about; any past that are as likely as nothing.</summary>
    public const int MostAsked = 16;

    private const int LongestSummary = 4_000;

    /// <summary>
    /// Each of <paramref name="issues"/>' likelihood, from 0 to 1, in their order; null where
    /// there are fewer than two to put in order or no model answered any.
    /// </summary>
    public async Task<IReadOnlyList<double>?> Likelihoods(IReadOnlyList<string> issues, CancellationToken cancel)
    {
        if (issues.Count < 2) return null;

        var decided = await Task.WhenAll(issues
            .Take(MostAsked)
            .Select(issue => decisions.Ask(DecisionUse.Issues, DecisionRequest.One(issue, "why", new Decide.Question.YesNo(Question)), cancel)))
            .ConfigureAwait(false);

        if (decided.All(d => d is null)) return null;

        return [.. issues.Select((_, i) => i < decided.Length && decided[i]?.Answers.GetValueOrDefault("why") is Answer.YesNo yes ? yes.Probability : 0)];
    }

    /// <summary>What a patch holds, short enough to be a state: its modules by name, and how many wires join them.</summary>
    public static string Summary(Patch patch, ModuleCatalog modules)
    {
        var names = patch.Nodes.Select(n => modules.Get(n.TypeId)?.Name ?? n.TypeId);
        var said = $"A patch of {Count(patch.Nodes.Count, "module")} and {Count(patch.Connections.Count, "wire")}: {string.Join(", ", names)}.";

        return said.Length > LongestSummary ? said[..LongestSummary] : said;
    }

    private static string Count(int n, string what) => n == 1 ? $"1 {what}" : $"{n} {what}s";

    /// <summary><paramref name="items"/> likeliest first, the order they came in where two tie.</summary>
    public static IReadOnlyList<T> Ordered<T>(IReadOnlyList<T> items, IReadOnlyList<double> likely) =>
        [.. items.Select((item, i) => (item, p: i < likely.Count ? likely[i] : 0, i)).OrderByDescending(x => x.p).ThenBy(x => x.i).Select(x => x.item)];
}
