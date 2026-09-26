using Flyback.App.Controls;

namespace Flyback.App;

internal sealed class WindowFocus : IWindowFocus
{
    public bool IsActive => MainWindowLocator.Owner.IsActive;
}