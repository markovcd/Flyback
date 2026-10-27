using System.Runtime.InteropServices.JavaScript;

namespace Flyback.Editor.Web;

/// <summary>The browser's own full screen, which the page takes while the picture has it, and where the picture's canvas stands.</summary>
internal static partial class PageScreen
{
    /// <summary>Asks the browser for the whole screen, or gives it back; a browser that refuses leaves the page as it was.</summary>
    [JSImport("wholeScreen", PageModule.Name)]
    public static partial void Take(bool whole);

    /// <summary>Puts the picture's canvas behind the editor's, so what the editor draws over the picture shows, or back above it.</summary>
    [JSImport("pictureBehind", PageModule.Name)]
    public static partial void PictureBehind(bool behind);
}
