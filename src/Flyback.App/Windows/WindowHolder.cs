namespace Flyback.App.Windows;

/// <summary>
/// Exist to break a cycle in dependency graph.
/// </summary>
internal sealed class WindowHolder
{
    public Avalonia.Controls.Window Instance => window ?? throw new InvalidOperationException("The window is not open yet.");
    private Avalonia.Controls.Window? window;
    public void Attach(Avalonia.Controls.Window value) => window = value;
}