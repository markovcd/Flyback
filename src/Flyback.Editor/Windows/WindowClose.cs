namespace Flyback.App.Windows;

internal sealed class WindowClose(WindowHolder holder) : IClose
{
    public void Close() => holder.Window.Close();
}