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