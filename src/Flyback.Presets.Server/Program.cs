using System.Threading.RateLimiting;
using Flyback.Presets.Server;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.FileProviders;

const long UploadLimit = 20 * 1024 * 1024;
const int PageSize = 24;
const int PendingLimit = 50;

// The output's wwwroot, where the Pages site's linked assets land beside the pages.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot"),
});

builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = UploadLimit);

// Behind the NAS's reverse proxy, the client is whoever the proxy says it is.
builder.Services.Configure<ForwardedHeadersOptions>(forwarded =>
{
    forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    forwarded.KnownIPNetworks.Clear();
    forwarded.KnownProxies.Clear();
});

builder.Services.AddRateLimiter(limits =>
{
    limits.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limits.AddPolicy("submit", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = http.RequestServices.GetRequiredService<IConfiguration>().GetValue("Presets:PostsPerHour", 20),
            Window = TimeSpan.FromHours(1),
        }));
});

var app = builder.Build();

string Setting(string name, string otherwise) =>
    Path.GetFullPath(app.Configuration[name] ?? otherwise, app.Environment.ContentRootPath);

var store = new PresetStore(Setting("Presets:Database", "/data/presets.db"));
var media = new MediaFolder(Setting("Presets:Media", "/media"));

Directory.CreateDirectory(media.Root);

app.UseForwardedHeaders();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(media.Root),
    RequestPath = MediaFolder.Route,
});
app.UseRateLimiter();

object View(StoredPreset preset) => new
{
    preset.Id,
    preset.Name,
    preset.Author,
    preset.Description,
    preset.Tags,
    preset.FileName,
    preset.Size,
    preset.Submitted,
    preset.Downloads,
    File = $"/api/v1/presets/{preset.Id}/file",
    Media = media.Of(preset.Id),
};

var api = app.MapGroup("/api/v1");

api.MapGet("/presets", (string? q, string? tag, int? page, bool? pending) =>
{
    if (pending == true)
    {
        var waiting = store.Oldest().Where(p => media.Pending(p.Id)).Take(PendingLimit).Select(View).ToList();

        return Results.Ok(new { Items = waiting, Total = waiting.Count, Page = 1, PageSize = PendingLimit });
    }

    var at = Math.Max(1, page ?? 1);
    var found = store.List(q, tag, at, PageSize);

    return Results.Ok(new { Items = found.Items.Select(View), found.Total, Page = at, PageSize });
});

api.MapGet("/presets/{id}", (string id) =>
    store.Find(id) is { } preset ? Results.Ok(View(preset)) : Results.NotFound());

// The render app fetches with count=false, so its fetches are not downloads.
api.MapGet("/presets/{id}/file", (string id, bool? count) =>
    store.Download(id, count != false) is { } download
        ? Results.File(download.File, "application/octet-stream", download.Preset.FileName)
        : Results.NotFound());

api.MapGet("/tags", () => Results.Ok(store.Tags(60).Select(t => new { t.Tag, t.Count })));

api.MapPost("/presets", async (HttpRequest request) =>
{
    if (!request.HasFormContentType) return Results.BadRequest(new { Error = "Send the preset as a form with a file field." });

    var form = await request.ReadFormAsync();

    if (form.Files.GetFile("file") is not { Length: > 0 } file)
        return Results.BadRequest(new { Error = "There is no file in the form." });

    using var bytes = new MemoryStream();
    await file.CopyToAsync(bytes);

    if (Submissions.Read(file.FileName, bytes.ToArray(), form["name"]) is not { } submission)
        return Results.BadRequest(new { Error = "That is not a Flyback patch. Send a .fbk or .fbkb file." });

    var stored = store.Add(submission, DateTimeOffset.UtcNow);

    return Results.Created($"/api/v1/presets/{stored.Id}", View(stored));
})
.DisableAntiforgery()
.RequireRateLimiting("submit");

app.Run();

/// <summary>The entry point, named so the tests can host it.</summary>
public partial class Program;
