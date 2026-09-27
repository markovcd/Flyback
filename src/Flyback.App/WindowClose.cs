namespace Flyback.App;

internal sealed class WindowClose(WindowHolder holder) : IWindowClose
{
    public void Close() => holder.Instance.Close();
}