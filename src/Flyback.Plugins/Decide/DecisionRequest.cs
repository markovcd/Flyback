namespace Flyback.Plugins.Decide;

/// <summary>A state, and the questions to answer about it.</summary>
/// <param name="State">The text the questions are about.</param>
/// <param name="Questions">Each question by an id its answer comes back under.</param>
public sealed record DecisionRequest(string State, IReadOnlyDictionary<string, Question> Questions)
{
    /// <summary>The most questions one request may ask.</summary>
    internal const int MostQuestions = 64;

    /// <summary>The longest a state may be, in characters.</summary>
    internal const int LongestState = 50_000;

    /// <summary>One question, under the id <paramref name="id"/>.</summary>
    internal static DecisionRequest One(string state, string id, Question question) =>
        new(state, new Dictionary<string, Question>(StringComparer.Ordinal) { [id] = question });

    /// <summary>Why no model should be asked this, or null where it may be.</summary>
    public string? Problem()
    {
        if (State is null) return "There is no state to ask about.";
        if (State.Length > LongestState) return $"The state is {State.Length:N0} characters; the most is {LongestState:N0}.";
        if (Questions is null || Questions.Count == 0) return "There is no question.";
        if (Questions.Count > MostQuestions) return $"There are {Questions.Count} questions; the most is {MostQuestions}.";

        foreach (var (id, question) in Questions)
        {
            if (string.IsNullOrWhiteSpace(id)) return "A question has no id.";
            if (question is null || string.IsNullOrWhiteSpace(question.Instructions)) return $"Question '{id}' asks nothing.";

            switch (question)
            {
                case Question.Choice choice when choice.Options is null || choice.Options.Count < 2:
                    return $"Question '{id}' is a choice with fewer than two options.";
                case Question.Choice choice when choice.Options.Select(o => o.Label).Distinct(StringComparer.Ordinal).Count() != choice.Options.Count
                                                 || choice.Options.Any(o => string.IsNullOrWhiteSpace(o.Label)):
                    return $"Question '{id}' has an option with no label, or two with the same one.";
                case Question.Score score when score.Levels is null || score.Levels.Count < 2:
                    return $"Question '{id}' is a score with fewer than two levels.";
            }
        }

        return null;
    }
}
