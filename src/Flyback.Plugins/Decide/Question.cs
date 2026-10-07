namespace Flyback.Plugins.Decide;

/// <summary>One question a decision model is asked about a state.</summary>
/// <param name="Instructions">The question, as a sentence.</param>
public abstract record Question(string Instructions)
{
    /// <summary>Which one of <paramref name="Options"/> fits, in the order given.</summary>
    public sealed record Choice(string Instructions, IReadOnlyList<ChoiceOption> Options) : Question(Instructions);

    /// <summary>Where on an ordered scale the state sits, lowest first.</summary>
    public sealed record Score(string Instructions, IReadOnlyList<string> Levels) : Question(Instructions);

    /// <summary>How likely the statement in <paramref name="Instructions"/> holds.</summary>
    public sealed record YesNo(string Instructions) : Question(Instructions);
}
