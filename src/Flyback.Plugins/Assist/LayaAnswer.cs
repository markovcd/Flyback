namespace Flyback.Plugins.Assist;

/// <summary>A typed answer and its probability information.</summary>
public abstract class LayaAnswer
{
    /// <summary>The selected option and its probability distribution.</summary>
    public sealed class Choice(string value, IReadOnlyDictionary<string, double> probabilities) : LayaAnswer
    {
        /// <summary>Selected option identifier.</summary>
        public string Value { get; } = value;

        /// <summary>Probabilities keyed by option identifier.</summary>
        public IReadOnlyDictionary<string, double> Probabilities { get; } = probabilities;
    }

    /// <summary>The expected ordinal value and probabilities for each ordered level.</summary>
    public sealed class Score(double value, IReadOnlyList<double> probabilities) : LayaAnswer
    {
        /// <summary>Expected score over the ordered levels.</summary>
        public double Value { get; } = value;

        /// <summary>Probabilities aligned with the ordered criteria.</summary>
        public IReadOnlyList<double> Probabilities { get; } = probabilities;
    }

    /// <summary>The estimated probability that the proposition is true.</summary>
    public sealed class Noul(double probability) : LayaAnswer
    {
        /// <summary>Estimated probability that the proposition is true.</summary>
        public double Probability { get; } = probability;
    }
}
