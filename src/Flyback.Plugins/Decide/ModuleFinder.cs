using Flyback.Core.Graph;

namespace Flyback.Plugins.Decide;

/// <summary>
/// Finds the modules a phrase describes, by meaning rather than by spelling: one choice of
/// category, then the modules of the likeliest two, ten to a question with a way to say none.
/// </summary>
/// <remarks>
/// The category's confidence is not gated on, only its order: a choice of fifteen is past
/// where the model's confidence is calibrated. A module is kept only where it beat "none of these".
/// </remarks>
internal sealed class ModuleFinder(Decisions decisions)
{
    public const int PerQuestion = 10;

    public const int MostFound = 8;

    private const string None = "none";

    private const int LongestDescription = 160;

    /// <summary>Whether a phrase is worth asking about: nothing it spells matches, or it is more than a name.</summary>
    public static bool Wanted(string phrase, int spelled) =>
        phrase.Trim().Length >= 3 && (spelled == 0 || phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 2);

    /// <summary>The modules among <paramref name="candidates"/> that <paramref name="phrase"/> describes, likeliest first; none without a model.</summary>
    public async Task<IReadOnlyList<FoundModule>> Find(string phrase, IReadOnlyList<NodeDef> candidates, CancellationToken cancel)
    {
        phrase = phrase.Trim();

        var categories = candidates.GroupBy(d => d.Category, StringComparer.Ordinal).ToList();

        if (phrase.Length == 0 || categories.Count == 0) return [];

        IEnumerable<string> likeliest = categories.Select(c => c.Key);

        if (categories.Count > 2)
        {
            var kinds = new Question.Choice(
                "Which kind of module does this describe?",
                [.. categories.Select(c => new ChoiceOption(c.Key, "Such as " + string.Join(", ", c.Take(6).Select(d => d.Name))))]);

            if (await decisions.Ask(DecisionRequest.One(phrase, "kind", kinds), cancel).ConfigureAwait(false) is not { } decision
                || decision.Answers.GetValueOrDefault("kind") is not Answer.Chosen kind)
                return [];

            likeliest = kind.Probabilities.OrderByDescending(p => p.Value).Take(2).Select(p => p.Key);
        }

        var wanted = likeliest.ToHashSet(StringComparer.Ordinal);
        var pool = candidates.Where(d => wanted.Contains(d.Category)).ToList();
        var chunks = pool.Chunk(PerQuestion).Take(DecisionRequest.MostQuestions).ToList();

        var questions = new Dictionary<string, Question>(StringComparer.Ordinal);

        for (var i = 0; i < chunks.Count; i++)
            questions[$"modules{i}"] = new Question.Choice(
                "Which module does this describe?",
                [.. chunks[i].Select(d => new ChoiceOption(d.TypeId, Described(d))), new ChoiceOption(None, "None of these")]);

        if (await decisions.Ask(new DecisionRequest(phrase, questions), cancel).ConfigureAwait(false) is not { } ranked) return [];

        var byId = pool.ToDictionary(d => d.TypeId, StringComparer.Ordinal);
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

    private static string Described(NodeDef def)
    {
        var said = def.Description.Length > LongestDescription ? def.Description[..LongestDescription] + "…" : def.Description;

        return said.Length == 0 ? def.Name : $"{def.Name}: {said}";
    }
}
