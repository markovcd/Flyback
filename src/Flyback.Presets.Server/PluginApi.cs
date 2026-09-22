using Flyback.Plugins.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace Flyback.Presets.Server;

/// <summary>What the site and the editor ask about shared plugins, under <c>/api/v1/plugins</c>.</summary>
/// <remarks>
/// A submitted package waits unpublished until a reviewer publishes it, and until then
/// nobody else can list it, open its page or download it. The listing is shaped for the
/// editor to download from too: filtered by <c>platform</c>, each with the SHA-256 the
/// downloaded bytes must hash to.
/// </remarks>
internal static class PluginApi
{
    public const int PageSize = 24;

    public static void MapPlugins(this RouteGroupBuilder api, PluginStore store, ReportStore reports, Func<HttpContext, bool> reviewing)
    {
        api.MapGet("/plugins", (HttpContext http, string? q, string? tag, string? platform, string? module, int? page) =>
        {
            if (!string.IsNullOrEmpty(platform) && !PluginPackage.Platforms.Contains(platform))
                return Results.BadRequest(new { Error = $"A platform is one of {string.Join(", ", PluginPackage.Platforms)}." });

            var at = Math.Max(1, page ?? 1);
            var found = store.List(q, platform, at, PageSize, reviewing(http), tag, module);

            return Results.Ok(new { Items = found.Items.Select(View), found.Total, Page = at, PageSize });
        });

        api.MapGet("/plugins/{id}", (HttpContext http, string id) =>
            store.Find(id, reviewing(http)) is { } plugin ? Results.Ok(View(plugin)) : Results.NotFound());

        api.MapGet("/plugins/{id}/file", (HttpContext http, string id, bool? count) =>
            store.Download(id, count != false, reviewing(http)) is { } download
                ? Results.File(download.File, "application/octet-stream", download.Plugin.FileName)
                : Results.NotFound());

        api.MapGet("/plugins/{id}/preview", (HttpContext http, string id) =>
        {
            if (store.Preview(id, reviewing(http)) is not { } preview) return Results.NotFound();

            http.Response.Headers.XContentTypeOptions = "nosniff";
            http.Response.Headers.CacheControl = "public, max-age=86400";

            return Results.File(preview.Bytes, preview.Type);
        });

        api.MapPost("/plugins", async (HttpRequest request) =>
        {
            if (!request.HasFormContentType) return Results.BadRequest(new { Error = "Send the plugin as a form with a file field." });

            var form = await request.ReadFormAsync();

            if (form.Files.GetFile("file") is not { Length: > 0 } file)
                return Results.BadRequest(new { Error = "There is no file in the form." });

            if (file.Length > PluginSubmissions.Limits.Packed)
                return Results.Json(new { Error = "That package is too large." }, statusCode: StatusCodes.Status413PayloadTooLarge);

            using var bytes = new MemoryStream();
            await file.CopyToAsync(bytes);

            PluginSubmission submission;

            try
            {
                submission = PluginSubmissions.Read(file.FileName, bytes.ToArray());
            }
            catch (InvalidDataException refused)
            {
                return Results.BadRequest(new { Error = refused.Message });
            }

            if (PackageSigner.Checked && store.TakenByAnother(submission.Assembly, submission.Signer))
                return Results.Conflict(new { Error = $"A published plugin is already called {submission.Assembly}, and was signed with another key." });

            if (store.Add(submission, DateTimeOffset.UtcNow) is not { } stored)
                return Results.Conflict(new { Error = "That package has been sent already." });

            return Results.Accepted($"/api/v1/plugins/{stored.Id}", View(stored));
        })
        .DisableAntiforgery()
        .RequireRateLimiting("submit")
        .WithMetadata(new RequestSizeLimitAttribute(PluginSubmissions.Limits.Packed + (1 << 20)));

        api.MapPatch("/plugins/{id}", (HttpContext http, string id, PluginChange change) =>
        {
            if (!reviewing(http)) return Results.Unauthorized();

            if (change.Published is true
                && PackageSigner.Checked
                && store.Find(id, unpublished: true) is { } publishing
                && store.TakenByAnother(publishing.Assembly, publishing.Signer is null ? null : new PackageSigner(publishing.Signer), except: id))
                return Results.Conflict(new { Error = $"A published plugin is already called {publishing.Assembly}, and was signed with another key." });

            if (change.Published is { } published && !store.Publish(id, published)) return Results.NotFound();

            return store.Find(id, unpublished: true) is { } plugin ? Results.Ok(View(plugin)) : Results.NotFound();
        });

        api.MapReport("/plugins/{id}/reports", ReportStore.Plugin, reports, id => store.Find(id) is not null);

        api.MapDelete("/plugins/{id}", (HttpContext http, string id) =>
        {
            if (!reviewing(http)) return Results.Unauthorized();
            if (!store.Delete(id)) return Results.NotFound();

            reports.Forget(ReportStore.Plugin, id);

            return Results.NoContent();
        });
    }

    private static object View(StoredPlugin plugin) => new
    {
        plugin.Id,
        plugin.Assembly,
        plugin.Name,
        plugin.Version,
        plugin.Author,
        plugin.Description,
        plugin.Tags,
        plugin.Adds,
        plugin.Reaches,
        plugin.Builds,
        plugin.Contract,
        Modules = plugin.Modules.Select(m => new { Id = m.TypeId, m.Name }),
        plugin.Sha256,
        Signer = plugin.Signer is null ? null : new PackageSigner(plugin.Signer).Fingerprint,
        plugin.FileName,
        plugin.Size,
        plugin.Submitted,
        plugin.Downloads,
        plugin.Published,
        File = $"/api/v1/plugins/{plugin.Id}/file",
        Preview = plugin.PreviewType is null ? null : $"/api/v1/plugins/{plugin.Id}/preview",
    };
}

internal sealed record PluginChange(bool? Published);
