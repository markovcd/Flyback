namespace Flyback.Plugins.Decide;

/// <summary>What a decision model answered a <see cref="DecisionRequest"/> with.</summary>
/// <param name="Model">Which model answered, as it names itself.</param>
/// <param name="Answers">One answer per question, by the question's id.</param>
/// <param name="Usage">What it cost.</param>
public sealed record Decision(string Model, IReadOnlyDictionary<string, Answer> Answers, DecisionUsage Usage);
