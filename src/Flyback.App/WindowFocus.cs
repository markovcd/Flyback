using Flyback.App.Controls;

namespace Flyback.App;

internal sealed class WindowFocus(MainWindowLocator locator) : IWindowFocus
{
    public bool IsActive => locator.Owner.IsActive;
}