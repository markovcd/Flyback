namespace Flyback.Plugins.Assist;

/// <summary>How hard an assistant should think before answering.</summary>
/// <remarks>
/// Three words rather than a number, because every provider spells this
/// differently and some do not offer it at all. An adapter maps what it can and
/// ignores the rest.
/// </remarks>
public enum AssistantEffort
{
    /// <summary>Answer quickly, with little thought.</summary>
    Low = 0,
    /// <summary>The default: a balance of speed and thought.</summary>
    Medium = 1,
    /// <summary>Think hardest, and take longest.</summary>
    High = 2,
}