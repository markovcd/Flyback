namespace Flyback.App;

internal sealed class WindowFocus(WindowHolder holder) : IWindowFocus
{
    public bool IsActive => holder.Instance.IsActive;
}