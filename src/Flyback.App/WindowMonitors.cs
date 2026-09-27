using Avalonia.Platform;
using Flyback.App.Controls;

namespace Flyback.App;

internal sealed class WindowMonitors(MainWindowLocator locator) : IMonitors
{
    public IReadOnlyList<Screen> All => locator.Owner.Screens?.All ?? [];
}