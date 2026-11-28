using System.Net;
using System.Security.Cryptography;
using System.Text;
using Flyback.Plugins.Hosting;

namespace Flyback.Editor.Tests.Packs;

/// <summary>A preset site sharing one pack, whatever it is asked, whose file may be made to differ from what it lists.</summary>
internal sealed class FakePackSite(string text, string? served = null) : HttpMessageHandler
{
    public static Uri Root { get; } = new("https://presets.example.org/");

    /// <summary>Whether the site lists nothing at all.</summary>
    public bool Empty { get; init; }

    public PackSite Site() => new(new HttpClient(this), Root);

    public SitePack Listed => PackSite.Read(System.Text.Json.JsonElement.Parse(Listing()), Root).Items[0];

    private string Listing()
    {
        if (Empty) return """{ "items": [], "total": 0, "page": 1, "pageSize": 24 }""";

        var header = text.Split('\n', 2)[0].Split(' ');

        return $$"""
            { "items": [ { "id": "v1", "pack": "{{header[1]}}", "version": "{{header[2]}}",
              "sha256": "{{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))}}",
              "file": "/api/v1/packs/v1/file", "modules": [ { "id": "{{header[1]}}.wobble", "name": "Wobble" } ] } ],
              "total": 1, "page": 1, "pageSize": 24 }
            """;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(request.RequestUri!.AbsolutePath == "/api/v1/packs/v1/file"
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(served ?? text) }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Listing(), Encoding.UTF8, "application/json") });
}
