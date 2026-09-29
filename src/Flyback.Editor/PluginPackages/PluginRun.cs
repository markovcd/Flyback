namespace Flyback.App.PluginPackages;

/// <summary>What this run of Flyback found besides the plugins it lists.</summary>
/// <param name="Folder">Where plugins are looked for.</param>
/// <param name="Problems">What went wrong that no installed plugin can be blamed for.</param>
internal sealed record PluginRun(string Folder, IReadOnlyList<string> Problems);