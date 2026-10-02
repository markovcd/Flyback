namespace Flyback.Editor;

/// <summary>A window has no viewer beside it, and no View it on its toolbar.</summary>
internal sealed class NoViewer : IViewer
{
    public void Show(string name, Func<byte[]> pack)
    {
    }
}
