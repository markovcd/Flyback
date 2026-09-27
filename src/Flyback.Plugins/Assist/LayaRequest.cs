using System.Text.Json;

namespace Flyback.Plugins.Assist;

/// <summary>The state and typed questions sent to a local decision model.</summary>
public sealed class LayaRequest(JsonElement state, IReadOnlyList<LayaQuestion> questions)
{
    /// <summary>Structured evidence supplied to the model.</summary>
    public JsonElement State { get; } = state;

    /// <summary>The bounded questions to answer.</summary>
    public IReadOnlyList<LayaQuestion> Questions { get; } = questions;
}
