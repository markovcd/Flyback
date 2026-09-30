using Flyback.Core.Graph;

namespace Flyback.Server;

/// <summary>
/// Why the web viewer and editor cannot open a shared preset: the plugins it names that
/// they do not link, and how many of its modules they cannot build, named or not.
/// </summary>
internal sealed record BrowserLack(IReadOnlyList<ModuleProvider> Plugins, int Modules);
