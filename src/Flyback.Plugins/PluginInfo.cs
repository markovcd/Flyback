namespace Flyback.Plugins;

/// <summary>
/// What a plugin says about itself. <paramref name="Id"/> is stable and
/// machine-readable; it is what a setting or a log line names.
/// </summary>
public sealed record PluginInfo(string Id, string Name, string Description = "");