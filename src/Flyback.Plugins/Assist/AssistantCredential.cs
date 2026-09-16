using Flyback.Plugins.Settings;

namespace Flyback.Plugins.Assist;

/// <summary>
/// What a provider says about the key it needs, which the host holds and the
/// plugin never sees a place to store.
/// </summary>
/// <remarks>
/// Apart from <see cref="SettingField"/> because it is the one part of a
/// provider's configuration that must not become one (ADR-0034): the host reads
/// the variable and draws the box, and the key reaches a plugin only as
/// <see cref="AssistantConfig.ApiKey"/>, for the length of a run.
/// </remarks>
/// <param name="EnvironmentVariable">
/// The variable this provider is conventionally given its key in. The shell reads
/// it, not the plugin.
/// </param>
/// <param name="Help">One line saying where a key comes from, shown under the field.</param>
public sealed record AssistantCredential(string EnvironmentVariable, string Help);

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
