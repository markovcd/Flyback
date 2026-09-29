using Flyback.App;

namespace Flyback.WebEditor;

/// <summary>A page is closed by its tab, never by the editor, so asking does nothing.</summary>
internal sealed class PageClose : IClose
{
    public void Close()
    {
    }
}
