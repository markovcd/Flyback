namespace Flyback.Plugins.Decide;

/// <summary>
/// What a feature asks a decision model for. A use may lay settings of its own over the
/// model's, such as a checkpoint it is better on.
/// </summary>
internal static class DecisionUse
{
    /// <summary>Finding a module by what a phrase means.</summary>
    public const string Modules = "modules";

    /// <summary>Putting a patch's complaints in order.</summary>
    public const string Issues = "issues";

    /// <summary>Reading a message to the assistant, and the proposal it came back with.</summary>
    public const string Turns = "turns";

    /// <summary>Every use, with what it is for.</summary>
    public static readonly IReadOnlyDictionary<string, string> All = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [Modules] = "finding a module by meaning",
        [Issues] = "putting complaints in order",
        [Turns] = "reading an assistant turn",
    };
}
