using Flyback.Assist.Keys;
using Flyback.Plugins.Settings;

namespace Flyback.Assist;

/// <summary>
/// One configured provider, ready to be asked something.
/// </summary>
/// <remarks>
/// The two halves are kept apart because they are owned by different sides:
/// <see cref="Values"/> is what the provider asked for and reads back, and
/// <see cref="Client"/> is the host's, carrying a key the provider never holds (ADR-0034).
/// </remarks>
/// <param name="Client">What to send requests over, which signs them.</param>
/// <param name="Values">Every setting this provider declared, as it stands.</param>
public sealed record AssistantConfig(IKeyedClient Client, SettingValues Values)
{
    /// <summary>Nothing configured, which is what a provider is asked about before anybody has.</summary>
    internal static AssistantConfig Unset { get; } = new(KeyedClient.None, SettingValues.None);
}
