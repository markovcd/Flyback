using Flyback.Assist.Keys;
using Flyback.Plugins.Settings;

namespace Flyback.Assist.Decide;

/// <summary>One configured decision model, ready to be asked something.</summary>
/// <param name="Client">What to send requests over, which signs them (ADR-0158).</param>
/// <param name="Values">Every setting this model declared, as it stands.</param>
/// <param name="Folder">Where the host keeps this model's files, or null where it keeps none.</param>
public sealed record DecisionConfig(IKeyedClient Client, SettingValues Values, string? Folder);
