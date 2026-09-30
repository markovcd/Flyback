using System.Runtime.InteropServices.JavaScript;

namespace Flyback.WebEditor;

/// <summary>The preset site serving the page, one folder above it, which letters and shared presets go through.</summary>
internal static partial class PageSite
{
    public static Uri Root => new(Url());

    [JSImport("siteUrl", PageModule.Name)]
    private static partial string Url();
}
