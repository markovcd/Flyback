namespace Flyback.Cli.Rendering;

/// <summary>
/// Sends a preset's files to the site through its admin API, each under the name of its
/// suffix (<c>webp</c>, <c>peaks.json</c>), then <c>done</c> or <c>failed</c>.
/// </summary>
/// <remarks>
/// The site lists only what is still waiting, so whatever it lists is pending. A
/// render that is wanted again is put back in the queue on the site, not here.
/// </remarks>
internal sealed class MediaUpload(HttpClient site) : IPresetMedia
{
    public bool Pending(string id) => true;

    public async Task Put(string id, string suffix, string from, CancellationToken cancellation)
    {
        await using var file = File.OpenRead(from);
        await Send(id, suffix.TrimStart('.'), new StreamContent(file), cancellation);
    }

    public Task Done(string id, CancellationToken cancellation) => Send(id, "done", new ByteArrayContent([]), cancellation);

    public Task Failed(string id, string why, CancellationToken cancellation) => Send(id, "failed", new StringContent(why), cancellation);

    private async Task Send(string id, string name, HttpContent content, CancellationToken cancellation)
    {
        using (content)
        using (var sent = await site.PutAsync($"api/v1/admin/presets/{Uri.EscapeDataString(id)}/media/{name}", content, cancellation))
            sent.EnsureSuccessStatusCode();
    }
}
