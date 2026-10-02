using System.Runtime.InteropServices.JavaScript;
using Flyback.Editor.Gallery;

namespace Flyback.Editor.Web;

/// <summary>The site's stills, at <c>stills/</c> beside the page's own folder (ADR-0163).</summary>
internal sealed partial class PageStills : IStillShelf
{
    private static readonly HttpClient Site = new() { BaseAddress = new Uri(StillsUrl()) };

    public async Task<byte[]?> Read(string name)
    {
        try
        {
            using var response = await Site.GetAsync(new Uri(Uri.EscapeDataString(name), UriKind.Relative));

            return response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync() : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    [JSImport("stillsUrl", PageModule.Name)]
    private static partial string StillsUrl();
}
