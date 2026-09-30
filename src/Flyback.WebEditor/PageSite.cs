using System.Runtime.InteropServices.JavaScript;

namespace Flyback.WebEditor;

/// <summary>The preset site serving the page, one folder above it, which letters and shared presets go through.</summary>
internal static partial class PageSite
{
    public static Uri Root => new(Url());

    /// <summary>Leaves the editor for the site's front page, asking first where there is an edit to lose.</summary>
    [JSImport("goHome", PageModule.Name)]
    public static partial void Home();

    [JSImport("siteUrl", PageModule.Name)]
    private static partial string Url();
}
