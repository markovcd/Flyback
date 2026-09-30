using Flyback.Core.Graph;

namespace Flyback.Server;

/// <summary>The letter endpoints: anyone may write, only the admin reads and dismisses.</summary>
internal static class LetterApi
{
    public static void MapLetters(this RouteGroupBuilder api, LetterStore letters, Func<HttpContext, bool> reviewing)
    {
        api.MapPost("/letters", (LetterForm form) =>
        {
            if (form.Mood is not { } mood || !LetterStore.Moods.Contains(mood))
                return Results.BadRequest(new { Error = $"A mood is one of {string.Join(", ", LetterStore.Moods)}." });

            if (form.Message?.Trim() is not { Length: > 0 } message)
                return Results.BadRequest(new { Error = "A letter needs something in it." });

            if (message.Length > LetterStore.MessageLimit)
                return Results.BadRequest(new { Error = $"Say it in {LetterStore.MessageLimit} characters or fewer." });

            if (form.Contact?.Trim().Length > LetterStore.ContactLimit)
                return Results.BadRequest(new { Error = $"An address is {LetterStore.ContactLimit} characters or fewer." });

            letters.Add(mood, message, Said(form.Contact), Cut(form.Version), Cut(form.Platform), Cut(form.Plugins), DateTimeOffset.UtcNow);

            return Results.NoContent();
        })
        .DisableAntiforgery()
        .RequireRateLimiting("letter");

        api.MapGet("/letters", (HttpContext http) =>
            reviewing(http) ? Results.Ok(letters.List()) : Results.Unauthorized());

        api.MapDelete("/letters/{id}", (HttpContext http, string id) =>
            !reviewing(http) ? Results.Unauthorized()
            : letters.Dismiss(id) ? Results.NoContent()
            : Results.NotFound());
    }

    private static string? Said(string? text) => text?.Trim() is { Length: > 0 } said ? said : null;

    /// <summary>
    /// What the editor said about itself, kept but capped. It is filled in by the
    /// program rather than typed, so too much of it is a fault rather than a refusal.
    /// </summary>
    private static string? Cut(string? text) =>
        Said(text) is { } said ? TextLimit.Clip(said, LetterStore.AboutLimit) : null;
}
