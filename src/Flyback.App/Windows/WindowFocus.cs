namespace Flyback.App.Windows;

internal sealed class WindowFocus(WindowHolder holder) : IFocus
{
    public bool IsActive => holder.Instance.IsActive;
}