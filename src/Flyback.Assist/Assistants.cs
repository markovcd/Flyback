using Flyback.Assist.Gemini;
using Flyback.Assist.OpenAi;

namespace Flyback.Assist;

/// <summary>The assistants a host offers, the one to start on first.</summary>
internal sealed class Assistants(IReadOnlyList<IPatchAssistant> all)
{
    /// <summary>
    /// What the desktop editor and <c>flyback-cli</c> offer. Chat completions first: it reaches
    /// the most endpoints, a local runtime among them, so it is the one most people have a key for.
    /// </summary>
    public static Assistants Shipped { get; } = new([new OpenAiAssistant(), new GeminiAssistant()]);

    /// <summary>What a host with no assistant offers.</summary>
    public static Assistants None { get; } = new([]);

    public IReadOnlyList<IPatchAssistant> All { get; } = all;

    public int Count => All.Count;

    /// <summary>The one a fresh install starts on, or null where there is none.</summary>
    public IPatchAssistant? Preferred => All.Count > 0 ? All[0] : null;

    /// <summary>The assistant a setting names, or null when there is no such one.</summary>
    public IPatchAssistant? Find(string id) =>
        All.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.Ordinal));
}
