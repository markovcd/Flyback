namespace Flyback.App;

internal sealed class WindowTitle(WindowHolder holder) : IWindowTitle
{
    public void Set(string title) => holder.Instance.Title = title;
}
