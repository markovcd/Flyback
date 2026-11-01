using System.Runtime.InteropServices.JavaScript;

namespace Flyback.Editor.Web;

/// <summary>The machine the page's browser runs on, as far as the page needs to know it.</summary>
internal static partial class PagePlatform
{
    /// <summary>Whether the browser is on a Mac, where a scroll pans and a pinch zooms.</summary>
    [JSImport("onMac", PageModule.Name)]
    public static partial bool OnMac();
}
