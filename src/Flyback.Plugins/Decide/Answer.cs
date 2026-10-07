namespace Flyback.Plugins.Decide;

/// <summary>What a decision model answered one <see cref="Question"/> with.</summary>
public abstract record Answer
{
    /// <summary>The likeliest option of a <see cref="Question.Choice"/>.</summary>
    /// <param name="Option">Its label.</param>
    /// <param name="Probabilities">Every option's probability, by label, in the question's order.</param>
    /// <param name="Confidence">How sure the model is, from 0 to 1.</param>
    public sealed record Chosen(string Option, IReadOnlyDictionary<string, double> Probabilities, double Confidence) : Answer;

    /// <summary>Where a <see cref="Question.Score"/>'s state sits.</summary>
    /// <param name="Score">The expected level, counting the lowest as 0.</param>
    /// <param name="Levels">The question's levels, lowest first.</param>
    /// <param name="Probabilities">Each level's probability, in the same order.</param>
    /// <param name="Confidence">How sure the model is, from 0 to 1.</param>
    public sealed record Scored(double Score, IReadOnlyList<string> Levels, IReadOnlyList<double> Probabilities, double Confidence) : Answer;

    /// <summary>How likely a <see cref="Question.YesNo"/>'s statement holds, from 0 to 1.</summary>
    public sealed record YesNo(double Probability) : Answer;
}
