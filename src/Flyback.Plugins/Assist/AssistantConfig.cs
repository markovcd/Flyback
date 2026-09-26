using Flyback.Plugins.Settings;

namespace Flyback.Plugins.Assist;

/// <summary>
/// One configured provider, ready to be asked something.
/// </summary>
/// <remarks>
/// The two halves are kept apart because they are owned by different sides:
/// <see cref="Values"/> is what the provider asked for and reads back, and
/// <see cref="ApiKey"/> is the host's and lives no longer than the run — never in
/// the settings file, never logged (ADR-0034).
/// </remarks>
/// <param name="Values">Every setting this provider declared, as it stands.</param>
public sealed record AssistantConfig(string ApiKey, SettingValues Values)
{
    /// <summary>Nothing configured, which is what a provider is asked about before anybody has.</summary>
    public static AssistantConfig Unset { get; } = new(string.Empty, SettingValues.None);
}