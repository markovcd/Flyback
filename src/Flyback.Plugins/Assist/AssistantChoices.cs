namespace Flyback.Plugins.Assist;

/// <summary>
/// What <see cref="AssistantSchema.Read"/> makes of a filled-in form. Plugin-internal.
/// </summary>
/// <param name="Vision">Whether the model may be shown a rendered frame.</param>
/// <param name="Hearing">
/// Whether the patch's sound may be listened to at all. Off by default, since only
/// some models take sound.
/// </param>
/// <param name="EarModel">
/// A second model that listens for the builder (ADR-0047), or null. Null means the
/// builder hears for itself if it can, and otherwise nothing is heard.
/// </param>
public sealed record AssistantChoices(
    string Model,
    string? BaseUrl = null,
    bool Vision = true,
    bool Hearing = false,
    string? EarModel = null,
    AssistantEffort Effort = AssistantEffort.Medium);