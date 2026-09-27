using Avalonia.Controls;

namespace Flyback.App.Controls;

internal sealed class MainWindowLocator
{
    public Window Owner => window ?? throw new InvalidOperationException("The window is not open yet.");
    private Window? window;
    public void Attach(Window value) => window = value;
}