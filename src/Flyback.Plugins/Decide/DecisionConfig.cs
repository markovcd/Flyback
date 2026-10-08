using Flyback.Plugins.Assist;
using Flyback.Plugins.Settings;

namespace Flyback.Plugins.Decide;

/// <summary>One configured decision model, ready to be asked something.</summary>
/// <param name="Transport">What to send requests over, which signs them (ADR-0158).</param>
/// <param name="Values">Every setting this model declared, as it stands.</param>
/// <param name="Folder">Where the host keeps this model's files, or null where it keeps none.</param>
public sealed record DecisionConfig(IAssistantTransport Transport, SettingValues Values, string? Folder)
{
    /// <summary>Nothing configured.</summary>
    internal static DecisionConfig Unset { get; } = new(KeyedTransport.None, SettingValues.None, null);
}
