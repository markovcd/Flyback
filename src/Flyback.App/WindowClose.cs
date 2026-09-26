using Flyback.App.Controls;

namespace Flyback.App;

internal sealed class WindowClose : IWindowClose
{
    public void Close() => MainWindowLocator.Owner.Close();
}