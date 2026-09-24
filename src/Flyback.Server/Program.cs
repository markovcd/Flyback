using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Flyback.Plugins.Hosting;
using Flyback.Server;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.FileProviders;

const long uploadLimit = 20 * 1024 * 1024;
const int pageSize = 24;
const int pendingLimit = 50;

// The output's wwwroot, where the Pages site's linked assets land beside the pages.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot"),
});

string Setting(string name, string otherwise) =>
    Path.GetFullPath(builder.Configuration[name] ?? otherwise, builder.Environment.ContentRootPath);

var database = Setting("Site:Database", "/data/presets.db");
var admin = new Admin(builder.Configuration["Site:Admin:User"], builder.Configuration["Site:Admin:Password"]);

builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = uploadLimit);

// Behind the NAS's reverse proxy, the client is whoever the proxy says it is —
// and only the proxy is asked. `X-Forwarded-For` is a header anybody can write,
// and the middleware rewrites `Connection.RemoteIpAddress` from it before the
// limiters below partition on that address: believed from any connection, it is
// a fresh allowance for every request, so twenty submissions an hour or ten
// guesses at the admin password become as many as somebody cares to send.
//
// The check only happens when there is something to check against — an empty
// KnownProxies and KnownIPNetworks is not a proxy nobody matches, it is the
// test being skipped.
//
// The default is the private ranges a container's proxy reaches it from, which
// is the other half of "nothing but the proxy may reach the port" in
// deploy/site/README.md. Site:KnownProxies replaces it with a
// comma-separated list of addresses or networks.
string[] believed =
    (builder.Configuration["Site:KnownProxies"] ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

if (believed.Length == 0)
    believed = ["127.0.0.0/8", "::1/128", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "fc00::/7"];

var knownProxies = new List<IPAddress>();
var knownNetworks = new List<System.Net.IPNetwork>();

// Eagerly, so a typo in the setting is a container that will not start rather
// than one whose limiters quietly count every request against one allowance.
foreach (var proxy in believed)
{
    if (IPAddress.TryParse(proxy, out var one)) knownProxies.Add(one);
    else if (System.Net.IPNetwork.TryParse(proxy, out var range)) knownNetworks.Add(range);
    else throw new InvalidOperationException($"Site:KnownProxies holds \"{proxy}\", which is neither an address nor a network.");
}

builder.Services.Configure<ForwardedHeadersOptions>(forwarded =>
{
    forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    // One hop: the entry the proxy itself appended, never one a client sent ahead of it.
    forwarded.ForwardLimit = 1;

    forwarded.KnownProxies.Clear();
    forwarded.KnownIPNetworks.Clear();

    foreach (var proxy in knownProxies) forwarded.KnownProxies.Add(proxy);
    foreach (var network in knownNetworks) forwarded.KnownIPNetworks.Add(network);
});

builder.Services.AddRateLimiter(limits =>
{
    limits.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limits.AddPolicy("submit", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = http.RequestServices.GetRequiredService<IConfiguration>().GetValue("Site:PostsPerHour", 20),
            Window = TimeSpan.FromHours(1),
        }));
    limits.AddPolicy("sign-in", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(15) }));
    limits.AddPolicy("report", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = http.RequestServices.GetRequiredService<IConfiguration>().GetValue("Site:ReportsPerHour", 10),
            Window = TimeSpan.FromHours(1),
        }));
    limits.AddPolicy("letter", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = http.RequestServices.GetRequiredService<IConfiguration>().GetValue("Site:LettersPerHour", 5),
            Window = TimeSpan.FromHours(1),
        }));
    limits.AddPolicy("rate", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = http.RequestServices.GetRequiredService<IConfiguration>().GetValue("Site:RatingsPerHour", 60),
            Window = TimeSpan.FromHours(1),
        }));
});

// Kept beside the database, so a restarted container does not sign the admin out.
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(Path.GetDirectoryName(database)!, "keys")));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(cookie =>
{
    cookie.Cookie.Name = "flyback-admin";
    cookie.Cookie.HttpOnly = true;
    cookie.Cookie.SameSite = SameSiteMode.Strict;
    cookie.ExpireTimeSpan = TimeSpan.FromDays(14);
    cookie.SlidingExpiration = true;
});

var app = builder.Build();

var store = new PresetStore(database);
var plugins = new PluginStore(database);

// A plugin built beside the site is signed with the release key, as a release signs it.
// A Debug run checks no keys and packs it unsigned where there is none; a Release run makes one.
Defaults.Seed(
    store,
    plugins,
    Setting("Site:Defaults", Path.Combine(AppContext.BaseDirectory, "Defaults")),
    Setting("Site:Builds", Path.Combine(AppContext.BaseDirectory, "plugins")),
    () => builder.Configuration[ReleaseKey.Variable] is { Length: > 0 } pem
        ? pem
        : ReleaseKey.Kept() ?? (PackageSigner.Checked ? ReleaseKey.Make() : null),
    DateTimeOffset.UtcNow);
var media = new MediaFolder(Setting("Site:Media", "/media"));

Directory.CreateDirectory(media.Root);

var reports = new ReportStore(database);
var ratings = new RatingStore(database);
var letters = new LetterStore(database);

app.UseForwardedHeaders();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(media.Root),
    RequestPath = MediaFolder.Route,
});
app.UseRateLimiter();
app.UseAuthentication();

bool Signed(HttpContext http) => admin.Enabled && http.User.Identity?.IsAuthenticated == true;

object View(StoredPreset preset, Rating rating) => new
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
    preset.Published,
    File = $"/api/v1/presets/{preset.Id}/file",
    Media = media.Of(preset.Id),
    Rating = new { rating.Average, rating.Count },
};

IEnumerable<object> Views(IEnumerable<StoredPreset> presets)
{
    var listed = presets.ToList();
    var rated = ratings.Of(ReportStore.Preset, listed.Select(p => p.Id));

    return listed.Select(p => View(p, rated.GetValueOrDefault(p.Id, Rating.None)));
}

var api = app.MapGroup("/api/v1");

api.MapPlugins(plugins, reports, ratings, reviewing: Signed);
api.MapReports(reports, reviewing: Signed);
api.MapLetters(letters, reviewing: Signed);
api.MapReport("/presets/{id}/reports", ReportStore.Preset, reports, id => store.Find(id) is not null);
api.MapRating("/presets/{id}/rating", ReportStore.Preset, ratings, id => store.Find(id) is not null);

api.MapGet("/presets", (HttpContext http, string? q, string? tag, int? page, bool? pending) =>
{
    if (pending == true)
    {
        var waiting = store.Oldest().Where(p => media.Pending(p.Id)).Take(pendingLimit).ToList();

        return Results.Ok(new { Items = Views(waiting), Total = waiting.Count, Page = 1, PageSize = pendingLimit });
    }

    var at = Math.Max(1, page ?? 1);
    var found = store.List(q, tag, at, pageSize, Signed(http));

    return Results.Ok(new { Items = Views(found.Items), found.Total, Page = at, PageSize = pageSize });
});

api.MapGet("/presets/{id}", (HttpContext http, string id) =>
    store.Find(id, Signed(http)) is { } preset ? Results.Ok(View(preset, ratings.Of(ReportStore.Preset, id))) : Results.NotFound());

// render-presets fetches with count=false, so its fetches are not downloads.
api.MapGet("/presets/{id}/file", (HttpContext http, string id, bool? count) =>
    store.Download(id, count != false, Signed(http)) is { } download
        ? Results.File(download.File, "application/octet-stream", download.Preset.FileName)
        : Results.NotFound());

api.MapGet("/tags", (HttpContext http) => Results.Ok(store.Tags(60, Signed(http)).Select(t => new { t.Tag, t.Count })));

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

    return Results.Created($"/api/v1/presets/{stored.Id}", View(stored, Rating.None));
})
.DisableAntiforgery()
.RequireRateLimiting("submit");

api.MapGet("/admin", (HttpContext http) => Results.Ok(new { admin.Enabled, SignedIn = Signed(http) }));

api.MapPost("/admin/session", async (HttpContext http, SignIn given) =>
{
    if (!admin.Enabled) return Results.NotFound();
    if (!admin.Accepts(given.User, given.Password)) return Results.Unauthorized();

    var who = new ClaimsIdentity([new Claim(ClaimTypes.Name, admin.User!)], CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(new ClaimsPrincipal(who));

    return Results.NoContent();
})
.RequireRateLimiting("sign-in");

api.MapDelete("/admin/session", async (HttpContext http) =>
{
    await http.SignOutAsync();

    return Results.NoContent();
});

api.MapPatch("/presets/{id}", (HttpContext http, string id, PresetChange change) =>
{
    if (!Signed(http)) return Results.Unauthorized();

    if (change.Name is not null)
    {
        if (Submissions.Named(change.Name) is not { } name) return Results.BadRequest(new { Error = "A preset needs a name." });
        if (!store.Rename(id, name)) return Results.NotFound();
    }

    if (change.Published is { } published && !store.Publish(id, published)) return Results.NotFound();

    return store.Find(id, unpublished: true) is { } preset ? Results.Ok(View(preset, ratings.Of(ReportStore.Preset, id))) : Results.NotFound();
});

api.MapDelete("/presets/{id}", (HttpContext http, string id) =>
{
    if (!Signed(http)) return Results.Unauthorized();
    if (!store.Delete(id)) return Results.NotFound();

    reports.Forget(ReportStore.Preset, id);
    ratings.Forget(ReportStore.Preset, id);

    return Results.NoContent();
});

app.Run();

internal sealed record SignIn(string? User, string? Password);

internal sealed record PresetChange(string? Name, bool? Published);

/// <summary>The entry point, named so the tests can host it.</summary>
public partial class Program;
