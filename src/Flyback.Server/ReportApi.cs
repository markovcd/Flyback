namespace Flyback.Server;

/// <summary>The report endpoints: anyone may report, only the admin reads and dismisses.</summary>
internal static class ReportApi
{
    /// <summary>Takes a report about what <paramref name="exists"/> says is there to see.</summary>
    public static RouteHandlerBuilder MapReport(this RouteGroupBuilder api, string route, string kind, ReportStore reports, Func<string, bool> exists) =>
        api.MapPost(route, (string id, ReportForm form) =>
        {
            if (form.Reason is not { } reason || !ReportStore.Reasons.Contains(reason))
                return Results.BadRequest(new { Error = $"A reason is one of {string.Join(", ", ReportStore.Reasons)}." });

            var details = form.Details?.Trim();

            if (details?.Length > ReportStore.DetailsLimit)
                return Results.BadRequest(new { Error = $"Say it in {ReportStore.DetailsLimit} characters or fewer." });

            if (!exists(id)) return Results.NotFound();

            reports.Add(kind, id, reason, string.IsNullOrEmpty(details) ? null : details, DateTimeOffset.UtcNow);

            return Results.NoContent();
        })
        .DisableAntiforgery()
        .RequireRateLimiting("report");

    /// <remarks>Under /admin too, where the pages ask the Worker that replaces this site.</remarks>
    public static void MapReports(this RouteGroupBuilder api, ReportStore reports, Func<HttpContext, bool> reviewing)
    {
        foreach (var at in new[] { "/reports", "/admin/reports" })
        {
            api.MapGet(at, (HttpContext http) =>
                reviewing(http) ? Results.Ok(reports.List()) : Results.Unauthorized());

            api.MapDelete(at + "/{id}", (HttpContext http, string id) =>
                !reviewing(http) ? Results.Unauthorized()
                : reports.Dismiss(id) ? Results.NoContent()
                : Results.NotFound());
        }
    }
}
