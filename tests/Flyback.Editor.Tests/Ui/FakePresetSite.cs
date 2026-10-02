using System.Net;
using System.Text;
using System.Text.Json;
using Flyback.Editor.Site;

namespace Flyback.Editor.Tests.Ui;

/// <summary>A preset as the fake site lists it.</summary>
internal sealed record Posted(
    string Id,
    string Name,
    string Author = "",
    string Description = "",
    string FileName = "",
    byte[]? File = null,
    double Average = 0,
    int Ratings = 0,
    string[]? Tags = null,
    byte[]? Still = null);

/// <summary>
/// <c>/api/v1/presets</c> answered in memory: narrowed the way the site narrows, on every
/// word of the name, author or description, with each preset's file served from
/// <see cref="Posted.File"/>.
/// </summary>
internal sealed class FakePresetSite(params Posted[] presets) : HttpMessageHandler
{
    public static readonly Uri Root = new("http://site.test/");

    public List<Uri> Asked { get; } = [];

    /// <summary>Each report posted, by the path it was posted to, with its JSON.</summary>
    public List<(string Path, string Body)> Reports { get; } = [];

    /// <summary>Answers every report with a 500, as a site that is down does.</summary>
    public bool RefuseReports { get; set; }

    public int PageSize { get; set; } = 24;

    /// <summary>Answers nothing at all, as a site that is down does.</summary>
    public bool Down { get; set; }

    /// <summary>Answers everything with this, as a proxy in front of a site that is down does.</summary>
    public HttpStatusCode? Answering { get; set; }

    /// <summary>The ids taken off the site: listed nowhere, and not found by id or file.</summary>
    public HashSet<string> TakenDown { get; } = [];

    /// <summary>What the site says it was downloaded, which the editor has no use for but keeps.</summary>
    public const long Downloads = 42;

    public PresetSite Site() => new(new HttpClient(this), Root);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;

        lock (Asked) Asked.Add(uri);

        if (Down) throw new HttpRequestException("No connection could be made.");

        if (Answering is { } status) return Task.FromResult(new HttpResponseMessage(status));

        var shared = presets.Where(p => !TakenDown.Contains(p.Id)).ToList();

        if (request.Method == HttpMethod.Post && uri.AbsolutePath.EndsWith("/reports", StringComparison.Ordinal)) return Task.FromResult(Report(request));

        if (uri.AbsolutePath == "/api/v1/presets") return Task.FromResult(Json(List(uri, shared)));

        if (shared.FirstOrDefault(p => uri.AbsolutePath == $"/api/v1/presets/{p.Id}") is { } one)
            return Task.FromResult(Json(Item(one)));

        if (shared.FirstOrDefault(p => uri.AbsolutePath == $"/media/{p.Id}.webp") is { Still: { } still })
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(still) });

        var posted = shared.FirstOrDefault(p => uri.AbsolutePath == $"/api/v1/presets/{p.Id}/file");

        return Task.FromResult(posted?.File is { } bytes
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
            : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private HttpResponseMessage Report(HttpRequestMessage request)
    {
        if (RefuseReports) return new HttpResponseMessage(HttpStatusCode.InternalServerError);

        lock (Reports) Reports.Add((request.RequestUri!.AbsolutePath, request.Content!.ReadAsStringAsync().GetAwaiter().GetResult()));

        return new HttpResponseMessage(HttpStatusCode.NoContent);
    }

    private object List(Uri uri, List<Posted> shared)
    {
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var page = int.Parse(query["page"] ?? "1");
        var words = (query["q"] ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var found = shared
            .Where(p => words.All(w => $"{p.Name} {p.Author} {p.Description}".Contains(w, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return new
        {
            items = found.Skip((page - 1) * PageSize).Take(PageSize).Select(Item),
            total = found.Count,
            page,
            pageSize = PageSize,
        };
    }

    /// <summary>One preset as the site lists it, which is the same shape asked for by id.</summary>
    private static object Item(Posted p) => new
    {
        id = p.Id,
        name = p.Name,
        author = p.Author,
        description = p.Description,
        tags = p.Tags ?? [],
        fileName = p.FileName.Length > 0 ? p.FileName : p.Name + ".fbk",
        downloads = Downloads,
        file = $"/api/v1/presets/{p.Id}/file",
        media = new { still = p.Still is null ? null : $"/media/{p.Id}.webp", state = p.Still is null ? "pending" : "ready" },
        rating = new { average = p.Average, count = p.Ratings },
    };

    private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
    };
}
