using Flyback.Core.Graph;

namespace Flyback.Plugins.Decide;

/// <summary>
/// Finds the modules a phrase describes, by meaning rather than by spelling: every module
/// asked about by name, ten to a question with a way to say none, in one request.
/// </summary>
/// <remarks>
/// Names alone, because a model reading descriptions settles on the first options it was
/// shown; and no category first, because a choice of a dozen or more is past where its
/// answer means anything. A module is kept only where it beat "none of these".
/// </remarks>
internal sealed class ModuleFinder(Decisions decisions)
{
    public const int PerQuestion = 10;

    public const int MostFound = 8;

    private const string None = "none";

    /// <summary>Whether a phrase is worth asking about: nothing it spells matches, or it is more than a name.</summary>
    public static bool Wanted(string phrase, int spelled) =>
        phrase.Trim().Length >= 3 && (spelled == 0 || phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 2);

    /// <summary>The modules among <paramref name="candidates"/> that <paramref name="phrase"/> describes, likeliest first; none without a model.</summary>
    public async Task<IReadOnlyList<FoundModule>> Find(string phrase, IReadOnlyList<NodeDef> candidates, CancellationToken cancel)
    {
        phrase = phrase.Trim();

        if (phrase.Length == 0 || candidates.Count == 0) return [];

        var asked = candidates.Take(PerQuestion * DecisionRequest.MostQuestions).ToList();
        var questions = new Dictionary<string, Question>(StringComparer.Ordinal);
        var chunks = asked.Chunk(PerQuestion).ToList();

        for (var i = 0; i < chunks.Count; i++)
            questions[$"modules{i}"] = new Question.Choice(
                "Which module does this describe?",
                [.. chunks[i].Select(d => new ChoiceOption(d.TypeId, d.Name)), new ChoiceOption(None, "None of these")]);

        if (await decisions.Ask(new DecisionRequest(phrase, questions), cancel).ConfigureAwait(false) is not { } ranked) return [];

        var byId = asked.ToDictionary(d => d.TypeId, StringComparer.Ordinal);
        var found = new List<FoundModule>();

        foreach (var answer in ranked.Answers.Values.OfType<Answer.Chosen>())
        {
            var none = answer.Probabilities.GetValueOrDefault(None);

            foreach (var (id, p) in answer.Probabilities)
                if (p > none && byId.TryGetValue(id, out var def))
                    found.Add(new FoundModule(def, p));
        }

        return [.. found.OrderByDescending(f => f.Probability).ThenBy(f => f.Module.Name, StringComparer.Ordinal).Take(MostFound)];
    }
}
