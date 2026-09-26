namespace Flyback.Plugins.Assist;

/// <summary>
/// What a run configured this way may be handed, which the host has to know
/// because the host builds the workbench.
/// </summary>
/// <remarks>
/// The one thing the App still asks a provider about its settings, and it asks in
/// terms of what happens rather than what was chosen: whether a frame may be shown,
/// and who is played the sound. Which model that is stays the provider's business.
/// </remarks>
/// <param name="Vision">Whether the model may be shown a rendered frame.</param>
/// <param name="Hearing">Who listens, and <see cref="Listener.None"/> for nobody.</param>
public readonly record struct AssistantSenses(bool Vision = true, Listener Hearing = Listener.None);