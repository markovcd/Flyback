using System.Runtime.InteropServices.JavaScript;

namespace Flyback.Editor.Web;

/// <summary>The browser's own full screen, which the page takes while the picture has it.</summary>
internal static partial class PageScreen
{
    /// <summary>Asks the browser for the whole screen, or gives it back; a browser that refuses leaves the page as it was.</summary>
    [JSImport("wholeScreen", PageModule.Name)]
    public static partial void Take(bool whole);
}
