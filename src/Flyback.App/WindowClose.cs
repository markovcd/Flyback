using Flyback.App.Controls;

namespace Flyback.App;

internal sealed class WindowClose(MainWindowLocator locator) : IWindowClose
{
    public void Close() => locator.Owner.Close();
}