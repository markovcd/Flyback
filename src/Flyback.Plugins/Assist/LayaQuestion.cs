namespace Flyback.Plugins.Assist;

/// <summary>A bounded question for a local decision model.</summary>
public abstract class LayaQuestion(string id, string instructions)
{
    /// <summary>Stable key matching the corresponding answer.</summary>
    public string Id { get; } = id;

    /// <summary>The bounded decision to make from the supplied state.</summary>
    public string Instructions { get; } = instructions;

    /// <summary>Choose one named option.</summary>
    public sealed class Choice(string id, string instructions, IReadOnlyDictionary<string, string> criteria)
        : LayaQuestion(id, instructions)
    {
        /// <summary>Allowed option identifiers and what each means.</summary>
        public IReadOnlyDictionary<string, string> Criteria { get; } = criteria;
    }

    /// <summary>Choose from ordered levels, from lowest to highest.</summary>
    public sealed class Score(string id, string instructions, IReadOnlyList<string> criteria)
        : LayaQuestion(id, instructions)
    {
        /// <summary>Ordered score levels, from lowest to highest.</summary>
        public IReadOnlyList<string> Criteria { get; } = criteria;
    }

    /// <summary>Estimate the probability that a proposition is true.</summary>
    public sealed class Noul(string id, string instructions) : LayaQuestion(id, instructions);
}
