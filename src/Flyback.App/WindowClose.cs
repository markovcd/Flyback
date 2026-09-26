namespace Flyback.App;

internal sealed class WindowClose(EditorWindow window) : IWindowClose
{
    public void Close() => window.Value.Close();
}
