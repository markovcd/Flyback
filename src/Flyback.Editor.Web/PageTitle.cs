using System.Runtime.InteropServices.JavaScript;
using Flyback.Editor;

namespace Flyback.Editor.Web;

/// <summary>The editor's title, on the page's tab.</summary>
internal sealed class PageTitle : ITitle
{
    public void Set(string title) => JSHost.GlobalThis.GetPropertyAsJSObject("document")?.SetProperty("title", title);
}
