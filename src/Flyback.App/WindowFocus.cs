namespace Flyback.App;

internal sealed class WindowFocus(EditorWindow window) : IWindowFocus
{
    public bool IsActive => window.Value.IsActive;
}