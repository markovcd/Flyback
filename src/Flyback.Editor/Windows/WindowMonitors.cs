using Avalonia.Platform;

namespace Flyback.App.Windows;

internal sealed class WindowMonitors(WindowHolder holder) : IMonitors
{
    public IReadOnlyList<Screen> All => holder.Instance.Screens.All;
}