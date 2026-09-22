using System.Security.Claims;
using System.Threading.RateLimiting;
using Flyback.Presets.Server;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
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

string Setting(string name, string otherwise) =>
    Path.GetFullPath(builder.Configuration[name] ?? otherwise, builder.Environment.ContentRootPath);

var database = Setting("Presets:Database", "/data/presets.db");
var admin = new Admin(builder.Configuration["Presets:Admin:User"], builder.Configuration["Presets:Admin:Password"]);

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
    limits.AddPolicy("sign-in", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(15) }));
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
var media = new MediaFolder(Setting("Presets:Media", "/media"));

Directory.CreateDirectory(media.Root);

var plugins = new PluginStore(database);

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
    preset.Published,
    File = $"/api/v1/presets/{preset.Id}/file",
    Media = media.Of(preset.Id),
};

var api = app.MapGroup("/api/v1");

api.MapPlugins(plugins, reviewing: Signed);

api.MapGet("/presets", (HttpContext http, string? q, string? tag, int? page, bool? pending) =>
{
    if (pending == true)
    {
        var waiting = store.Oldest().Where(p => media.Pending(p.Id)).Take(PendingLimit).Select(View).ToList();

        return Results.Ok(new { Items = waiting, Total = waiting.Count, Page = 1, PageSize = PendingLimit });
    }

    var at = Math.Max(1, page ?? 1);
    var found = store.List(q, tag, at, PageSize, Signed(http));

    return Results.Ok(new { Items = found.Items.Select(View), found.Total, Page = at, PageSize });
});

api.MapGet("/presets/{id}", (HttpContext http, string id) =>
    store.Find(id, Signed(http)) is { } preset ? Results.Ok(View(preset)) : Results.NotFound());

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

    return Results.Created($"/api/v1/presets/{stored.Id}", View(stored));
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

    return store.Find(id, unpublished: true) is { } preset ? Results.Ok(View(preset)) : Results.NotFound();
});

api.MapDelete("/presets/{id}", (HttpContext http, string id) =>
    !Signed(http) ? Results.Unauthorized()
    : store.Delete(id) ? Results.NoContent()
    : Results.NotFound());

app.Run();

internal sealed record SignIn(string? User, string? Password);

internal sealed record PresetChange(string? Name, bool? Published);

/// <summary>The entry point, named so the tests can host it.</summary>
public partial class Program;
