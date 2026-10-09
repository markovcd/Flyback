namespace Flyback.Editor.Windows;

internal sealed class WindowClose(WindowHolder holder) : IClose
{
    public void Close() => holder.Window.Close();
}
