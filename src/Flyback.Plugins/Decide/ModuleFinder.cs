using Flyback.Core.Graph;

namespace Flyback.Plugins.Decide;

/// <summary>
/// Finds the modules a phrase describes, by meaning rather than by spelling: every module
/// asked about by its name and its <see cref="NodeDef.Words"/>, ten to a question with a
/// way to say none, in two orders in one request, and the ten that did best asked about
/// once more together.
/// </summary>
/// <remarks>
/// The name and the words are the option's label, "Reverb (room, hall, space)", with no
/// description: a label is what the model keys on, and a description as long as a
/// sentence settles it on the first options it was shown. A choice of a dozen or more is
/// past where its answer means anything. Each module is asked about in the catalog's order
/// and in reverse, so it sits near the front once and near the back once, with other
/// neighbors each time, and its margin over "none of these" is averaged across the two. A
/// module is kept only where that average is above nothing, and the final question orders
/// the best of them, since a module's answer depends on which others it was asked beside.
/// </remarks>
internal sealed class ModuleFinder(Decisions decisions)
{
    public const int PerQuestion = 10;

    public const int MostFound = 8;

    /// <summary>How many orders each module is asked about in.</summary>
    public const int Orders = 2;

    /// <summary>How many of the best are asked about once more, together.</summary>
    public const int Finalists = 10;

    private const string None = "none";

    /// <summary>
    /// Whether <paramref name="phrase"/> is spelled in a module's name or <see cref="NodeDef.Words"/>:
    /// the whole phrase in the name, or each of its words of three letters or more somewhere in
    /// the two. Instant, and sure where it hits, so it comes before any model is asked.
    /// </summary>
    public static bool Spelled(NodeDef module, string phrase)
    {
        phrase = phrase.Trim();

        if (phrase.Length == 0) return false;
        if (module.Name.Contains(phrase, StringComparison.OrdinalIgnoreCase)) return true;

        var words = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 3).ToList();

        return words.Count > 0
            && words.All(w => module.Name.Contains(w, StringComparison.OrdinalIgnoreCase) || module.Words.Contains(w, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Whether a phrase is worth asking about: nothing it spells matches, or it is more than a name.</summary>
    public static bool Wanted(string phrase, int spelled) =>
        phrase.Trim().Length >= 3 && (spelled == 0 || phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 2);

    /// <summary>The modules among <paramref name="candidates"/> that <paramref name="phrase"/> describes, likeliest first; none without a model.</summary>
    public async Task<IReadOnlyList<FoundModule>> Find(string phrase, IReadOnlyList<NodeDef> candidates, CancellationToken cancel)
    {
        phrase = phrase.Trim();

        if (phrase.Length == 0 || candidates.Count == 0) return [];

        var asked = candidates.Take(PerQuestion * (DecisionRequest.MostQuestions / Orders)).ToList();
        var labels = Labels(asked);
        var questions = new Dictionary<string, Question>(StringComparer.Ordinal);
        IEnumerable<NodeDef>[] orders = [asked, Enumerable.Reverse(asked)];

        foreach (var order in orders)
            foreach (var chunk in order.Chunk(PerQuestion))
                questions[$"modules{questions.Count}"] = new Question.Choice(Asked, [.. chunk.Select(d => new ChoiceOption(labels[d], "")), new ChoiceOption(None, "None of these")]);

        if (await decisions.Ask(DecisionUse.Modules, new DecisionRequest(phrase, questions), cancel).ConfigureAwait(false) is not { } ranked) return [];

        var byLabel = labels.ToDictionary(l => l.Value, l => l.Key, StringComparer.Ordinal);
        var margins = new Dictionary<string, double>(StringComparer.Ordinal);
        var probabilities = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var answer in ranked.Answers.Values.OfType<Answer.Chosen>())
        {
            var none = answer.Probabilities.GetValueOrDefault(None);

            foreach (var (label, p) in answer.Probabilities)
            {
                if (!byLabel.ContainsKey(label)) continue;

                margins[label] = margins.GetValueOrDefault(label) + (p - none) / Orders;
                probabilities[label] = probabilities.GetValueOrDefault(label) + p / Orders;
            }
        }

        var best = margins
            .Where(m => m.Value > 0)
            .OrderByDescending(m => m.Value)
            .ThenBy(m => byLabel[m.Key].Name, StringComparer.Ordinal)
            .Select(m => m.Key)
            .ToList();

        var finalists = best.Take(Finalists).ToList();

        if (finalists.Count >= 2)
        {
            var final = new Question.Choice(Asked, [.. finalists.Select(l => new ChoiceOption(l, "")), new ChoiceOption(None, "None of these")]);

            if (await decisions.Ask(DecisionUse.Modules, DecisionRequest.One(phrase, "final", final), cancel).ConfigureAwait(false) is { } decided
                && decided.Answers.GetValueOrDefault("final") is Answer.Chosen chosen)
            {
                foreach (var label in finalists) probabilities[label] = chosen.Probabilities.GetValueOrDefault(label);

                best = [.. finalists.OrderByDescending(l => probabilities[l]).ThenBy(l => byLabel[l].Name, StringComparer.Ordinal), .. best.Skip(Finalists)];
            }
        }

        return [.. best.Take(MostFound).Select(l => new FoundModule(byLabel[l], probabilities[l]))];
    }

    private const string Asked = "Which module does this describe?";

    /// <summary>
    /// Each module's option label: its name with its words in brackets, told apart by category
    /// and then by type id where two would read the same.
    /// </summary>
    internal static Dictionary<NodeDef, string> Labels(IReadOnlyList<NodeDef> modules)
    {
        var labels = new Dictionary<NodeDef, string>();
        var taken = new HashSet<string>(StringComparer.Ordinal) { None };

        foreach (var module in modules)
        {
            var named = module.Words.Length > 0 ? $"{module.Name} ({module.Words})" : module.Name;

            var label = taken.Add(named) ? named
                : taken.Add($"{named} ({module.Category})") ? $"{named} ({module.Category})"
                : $"{named} ({module.TypeId})";

            taken.Add(label);
            labels[module] = label;
        }

        return labels;
    }
}
