using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flyback.App.PluginPackages;

namespace Flyback.App.Tests.PluginPackages;

/// <summary>A plugin as the fake site lists it.</summary>
internal sealed record Shared(
    string Id,
    string Name,
    string Assembly = "Flyback.Plugins.Shared",
    string Version = "1.0.0",
    string Author = "",
    string[]? Tags = null,
    string[]? Modules = null,
    byte[]? Package = null);

/// <summary>
/// <c>/api/v1/plugins</c> answered in memory: narrowed the way the site narrows,
/// with each plugin's file served from <see cref="Shared.Package"/>.
/// </summary>
internal sealed class FakePluginSite(params Shared[] plugins) : HttpMessageHandler
{
    public static readonly Uri Root = new("http://site.test/");

    public List<Uri> Asked { get; } = [];

    /// <summary>Served in place of every package, to show a download is checked against the listed hash.</summary>
    public byte[]? Tampered { get; set; }

    public int PageSize { get; set; } = 24;

    public PluginSite Site() => new(new HttpClient(this), Root);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;

        Asked.Add(uri);

        var path = uri.AbsolutePath;

        if (path == "/api/v1/plugins") return Task.FromResult(Json(List(uri)));

        var shared = plugins.FirstOrDefault(p => path == $"/api/v1/plugins/{p.Id}/file");

        return Task.FromResult(shared?.Package is { } bytes
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Tampered ?? bytes) }
            : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private object List(Uri uri)
    {
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var page = int.Parse(query["page"] ?? "1");

        var found = plugins
            .Select(p => (Shared: p, Listed: new ListedPlugin(p.Assembly, p.Name, p.Version, p.Author, "", p.Tags ?? [], p.Modules ?? [])))
            .Where(p => p.Listed.Matches(query["q"], query["tag"]))
            .ToList();

        return new
        {
            items = found.Skip((page - 1) * PageSize).Take(PageSize).Select(p => new
            {
                id = p.Shared.Id,
                assembly = p.Shared.Assembly,
                name = p.Shared.Name,
                version = p.Shared.Version,
                author = p.Shared.Author,
                description = "",
                tags = p.Shared.Tags ?? [],
                builds = new[] { "win", "osx", "linux" },
                modules = (p.Shared.Modules ?? []).Select(m => new { id = m.ToLowerInvariant(), name = m }),
                sha256 = Convert.ToHexStringLower(SHA256.HashData(p.Shared.Package ?? [])),
                size = p.Shared.Package?.Length ?? 0,
                downloads = 0,
                file = $"/api/v1/plugins/{p.Shared.Id}/file",
                preview = (string?)null,
            }),
            total = found.Count,
            page,
            pageSize = PageSize,
        };
    }

    private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
    };
}
