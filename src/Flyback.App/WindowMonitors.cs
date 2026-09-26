using Avalonia.Platform;
using Flyback.App.Controls;

namespace Flyback.App;

internal sealed class WindowMonitors : IMonitors
{
    public IReadOnlyList<Screen> All => MainWindowLocator.Owner.Screens?.All ?? [];
}