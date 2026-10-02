using System.Runtime.InteropServices.JavaScript;
using Flyback.Editor;

namespace Flyback.Editor.Web;

/// <summary>Whether the page is the one being typed into.</summary>
internal sealed partial class PageFocus : IFocus
{
    public bool IsActive => HasFocus();

    [JSImport("hasFocus", PageModule.Name)]
    private static partial bool HasFocus();
}
