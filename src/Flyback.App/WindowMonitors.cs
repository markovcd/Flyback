using Avalonia.Platform;

namespace Flyback.App;

internal sealed class WindowMonitors(EditorWindow window) : IMonitors
{
    public IReadOnlyList<Screen> All => window.Value.Screens?.All ?? [];
}