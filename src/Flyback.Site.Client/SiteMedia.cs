namespace Flyback.Site.Client;

/// <summary>A preset's renders, as the admin API takes them.</summary>
internal static class SiteMedia
{
    /// <summary>
    /// Sends <paramref name="content"/> as the preset's <paramref name="name"/>: a suffix
    /// (<c>webp</c>, <c>peaks.json</c>), or <c>done</c> or <c>failed</c> once the rest is there.
    /// </summary>
    /// <exception cref="HttpRequestException">The site did not take it, saying why.</exception>
    public static async Task Put(HttpClient site, string id, string name, HttpContent content, CancellationToken cancellation)
    {
        using (content)
        using (var answer = await site.PutAsync($"api/v1/admin/presets/{Uri.EscapeDataString(id)}/media/{name}", content, cancellation))
            await SiteAnswer.EnsureTaken(answer, cancellation);
    }
}
