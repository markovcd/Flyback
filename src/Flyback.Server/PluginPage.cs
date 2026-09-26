namespace Flyback.Server;

/// <summary>A page of plugins and how many match in all.</summary>
internal sealed record PluginPage(IReadOnlyList<StoredPlugin> Items, int Total);