using Flyback.Core.Graph;

namespace Flyback.Server;

/// <summary>
/// Why the web viewer and editor cannot open a shared preset: the plugins it names that
/// they do not link, and how many of its modules they cannot build, named or not.
/// </summary>
internal sealed record BrowserLack(IReadOnlyList<ModuleProvider> Plugins, int Modules)
{
    /// <summary>What is lacking, in a few words, as the presets page and the web editor's gallery say it.</summary>
    public string Said => Plugins.Select(plugin => plugin.Name).ToList() switch
    {
        [] => "Needs modules a browser lacks",
        [var one] => $"Needs the {one} plugin",
        var names => $"Needs the {string.Join(", ", names[..^1])} and {names[^1]} plugins",
    };
}
