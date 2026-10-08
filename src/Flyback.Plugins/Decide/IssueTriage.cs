using Flyback.Core.Graph;

namespace Flyback.Plugins.Decide;

/// <summary>
/// How likely each of a patch's complaints is to be why it is silent or dark, so the
/// likeliest can be said first. One score question per complaint, about the patch.
/// </summary>
internal sealed class IssueTriage(Decisions decisions)
{
    /// <summary>The scale each complaint is scored on, lowest first.</summary>
    public static readonly IReadOnlyList<string> Levels = ["not why", "might be why", "surely why"];

    private const int LongestSummary = 4_000;

    /// <summary>
    /// Each of <paramref name="issues"/>' likelihood, from 0 to 1, in their order; null where
    /// there are fewer than two to put in order or no model answered.
    /// </summary>
    public async Task<IReadOnlyList<double>?> Likelihoods(IReadOnlyList<string> issues, string patch, CancellationToken cancel)
    {
        if (issues.Count < 2) return null;

        var asked = issues.Take(DecisionRequest.MostQuestions).ToList();
        var questions = new Dictionary<string, Question>(StringComparer.Ordinal);

        for (var i = 0; i < asked.Count; i++)
            questions[$"issue{i}"] = new Question.Score($"How likely is this why the patch is silent or shows nothing: {asked[i]}", Levels);

        if (await decisions.Ask(DecisionUse.Issues, new DecisionRequest(patch, questions), cancel).ConfigureAwait(false) is not { } decision) return null;

        return [.. issues.Select((_, i) => decision.Answers.GetValueOrDefault($"issue{i}") is Answer.Scored scored
            ? Math.Clamp(scored.Score / (Levels.Count - 1), 0, 1)
            : 0)];
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
