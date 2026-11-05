using System.Runtime.InteropServices.JavaScript;

namespace Flyback.Editor.Web;

/// <summary>The machine the page's browser runs on, as far as the page needs to know it.</summary>
internal static partial class PagePlatform
{
    /// <summary>Whether the browser is on a Mac, where a scroll pans and a pinch zooms.</summary>
    [JSImport("onMac", PageModule.Name)]
    public static partial bool OnMac();

    /// <summary>Whether the browser's latest wheel event came from a mouse wheel rather than a trackpad, or null if it cannot tell.</summary>
    public static bool? ScrollIsMouse() => LastWheel() switch { 1 => true, 0 => false, _ => null };

    /// <summary>1 for a mouse wheel, 0 for a trackpad, -1 for no wheel event yet or a browser that does not say.</summary>
    [JSImport("lastWheel", PageModule.Name)]
    private static partial int LastWheel();
}
