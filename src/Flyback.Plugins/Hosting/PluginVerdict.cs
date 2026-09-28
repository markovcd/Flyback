namespace Flyback.Plugins.Hosting;

/// <summary>Whether a plugin folder loads, whether it may keep keys, and why.</summary>
/// <param name="Reason">Why it does not load, as a sentence that says what to do; null where it does.</param>
internal sealed record PluginVerdict(PluginStanding Standing, bool Secrets, string? Reason)
{
    public bool Loads => Standing is PluginStanding.Unchecked or PluginStanding.Shipped or PluginStanding.Allowed;
}
