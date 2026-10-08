using Flyback.Core.Graph;

namespace Flyback.Plugins.Decide;

/// <summary>
/// Finds the modules a phrase describes, by meaning rather than by spelling: every module
/// asked about by name, ten to a question with a way to say none, in two orders in one request.
/// </summary>
/// <remarks>
/// Names alone, because a model reading descriptions settles on the first options it was
/// shown; and no category first, because a choice of a dozen or more is past where its
/// answer means anything. Each module is asked about in the catalog's order and in reverse,
/// so it sits near the front once and near the back once, with other neighbors each time,
/// and its margin over "none of these" is averaged across the two. A module is kept only
/// where that average is above nothing.
/// </remarks>
internal sealed class ModuleFinder(Decisions decisions)
{
    public const int PerQuestion = 10;

    public const int MostFound = 8;

    /// <summary>How many orders each module is asked about in.</summary>
    public const int Orders = 2;

    private const string None = "none";

    /// <summary>Whether a phrase is worth asking about: nothing it spells matches, or it is more than a name.</summary>
    public static bool Wanted(string phrase, int spelled) =>
        phrase.Trim().Length >= 3 && (spelled == 0 || phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 2);

    /// <summary>The modules among <paramref name="candidates"/> that <paramref name="phrase"/> describes, likeliest first; none without a model.</summary>
    public async Task<IReadOnlyList<FoundModule>> Find(string phrase, IReadOnlyList<NodeDef> candidates, CancellationToken cancel)
    {
        phrase = phrase.Trim();

        if (phrase.Length == 0 || candidates.Count == 0) return [];

        var asked = candidates.Take(PerQuestion * (DecisionRequest.MostQuestions / Orders)).ToList();
        var questions = new Dictionary<string, Question>(StringComparer.Ordinal);
        IEnumerable<NodeDef>[] orders = [asked, Enumerable.Reverse(asked)];

        foreach (var order in orders)
            foreach (var chunk in order.Chunk(PerQuestion))
                questions[$"modules{questions.Count}"] = new Question.Choice(
                    "Which module does this describe?",
                    [.. chunk.Select(d => new ChoiceOption(d.TypeId, d.Name)), new ChoiceOption(None, "None of these")]);

        if (await decisions.Ask(DecisionUse.Modules, new DecisionRequest(phrase, questions), cancel).ConfigureAwait(false) is not { } ranked) return [];

        var byId = asked.ToDictionary(d => d.TypeId, StringComparer.Ordinal);
        var margins = new Dictionary<string, double>(StringComparer.Ordinal);
        var probabilities = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var answer in ranked.Answers.Values.OfType<Answer.Chosen>())
        {
            var none = answer.Probabilities.GetValueOrDefault(None);

            foreach (var (id, p) in answer.Probabilities)
            {
                if (!byId.ContainsKey(id)) continue;

                margins[id] = margins.GetValueOrDefault(id) + (p - none) / Orders;
                probabilities[id] = probabilities.GetValueOrDefault(id) + p / Orders;
            }
        }

        return [.. margins
            .Where(m => m.Value > 0)
            .OrderByDescending(m => m.Value)
            .ThenBy(m => byId[m.Key].Name, StringComparer.Ordinal)
            .Take(MostFound)
            .Select(m => new FoundModule(byId[m.Key], probabilities[m.Key]))];
    }
}
