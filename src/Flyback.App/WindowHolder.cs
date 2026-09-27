using Avalonia.Controls;

namespace Flyback.App;

/// <summary>
/// Exist to break a cycle in dependency graph.
/// </summary>
internal sealed class WindowHolder
{
    public Window Instance => window ?? throw new InvalidOperationException("The window is not open yet.");
    private Window? window;
    public void Attach(Window value) => window = value;
}