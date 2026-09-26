namespace Flyback.Plugins.Hosting;

/// <summary>A plugin that loaded, and where it came from.</summary>
internal sealed record LoadedPlugin(PluginInfo Info, string AssemblyPath);