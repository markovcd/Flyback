namespace Flyback.Server;

/// <summary>The rating endpoints: anyone reads a rating, and only a page of the site gives one.</summary>
internal static class RatingApi
{
    /// <summary>
    /// Reads and takes the rating of what <paramref name="exists"/> says is there to see,
    /// at <paramref name="route"/>.
    /// </summary>
    /// <remarks>
    /// A rating is taken only where the browser says the request came from the site's own
    /// page (<c>Sec-Fetch-Site: same-origin</c>), which the editor never sends: the editor
    /// shows ratings and leaves giving them to the site.
    /// </remarks>
    public static void MapRating(this RouteGroupBuilder api, string route, string kind, RatingStore ratings, Func<string, bool> exists)
    {
        object Said(string id, string voter)
        {
            var rating = ratings.Of(kind, id);

            return new { rating.Average, rating.Count, Mine = ratings.Mine(kind, id, voter) };
        }

        api.MapGet(route, (HttpContext http, string id) =>
            exists(id) ? Results.Ok(Said(id, ratings.Voter(http.Connection.RemoteIpAddress))) : Results.NotFound());

        api.MapPut(route, (HttpContext http, string id, RatingForm form) =>
        {
            if (!string.Equals(http.Request.Headers["Sec-Fetch-Site"], "same-origin", StringComparison.Ordinal))
                return Results.Json(new { Error = "Rate it on its page on the preset site." }, statusCode: StatusCodes.Status403Forbidden);

            if (form.Stars is not { } stars || stars is < 1 or > RatingStore.Most)
                return Results.BadRequest(new { Error = $"A rating is 1 to {RatingStore.Most} stars." });

            if (!exists(id)) return Results.NotFound();

            var voter = ratings.Voter(http.Connection.RemoteIpAddress);

            ratings.Rate(kind, id, voter, stars, DateTimeOffset.UtcNow);

            return Results.Ok(Said(id, voter));
        })
        .DisableAntiforgery()
        .RequireRateLimiting("rate");
    }
}
