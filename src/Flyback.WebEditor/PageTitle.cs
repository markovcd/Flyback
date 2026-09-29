using System.Runtime.InteropServices.JavaScript;
using Flyback.App;

namespace Flyback.WebEditor;

/// <summary>The editor's title, on the page's tab.</summary>
internal sealed class PageTitle : ITitle
{
    public void Set(string title) => JSHost.GlobalThis.GetPropertyAsJSObject("document")?.SetProperty("title", title);
}
