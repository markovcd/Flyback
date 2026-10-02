namespace Flyback.Editor.Windows;

internal sealed class WindowTitle(WindowHolder holder) : ITitle
{
    public void Set(string title) => holder.Window.Title = title;
}
