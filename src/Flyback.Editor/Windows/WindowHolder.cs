using Avalonia.Controls;

namespace Flyback.App.Windows;

/// <summary>
/// The window or page the editor is in, which exists to break a cycle in the dependency graph.
/// </summary>
internal sealed class WindowHolder
{
    private TopLevel? top;

    public TopLevel Instance => top ?? throw new InvalidOperationException("The editor is not in a window yet.");

    /// <summary>The desktop window the editor is in, for what only a window has.</summary>
    public Window Window => Instance as Window ?? throw new InvalidOperationException("The editor is not in a desktop window.");

    public void Attach(TopLevel value) => top = value;
}
