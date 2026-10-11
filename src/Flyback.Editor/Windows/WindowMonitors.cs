using Avalonia.Platform;
using Flyback.Editor.Shell;

namespace Flyback.Editor.Windows;

internal sealed class WindowMonitors(WindowHolder holder) : IMonitors
{
    public IReadOnlyList<Screen> All => holder.Instance.Screens?.All ?? [];
}
