using System.Net.Http.Json;

namespace Flyback.Site;

/// <summary>What the site said when it did not take a request: its status and the reason it gave.</summary>
internal static class SiteAnswer
{
    /// <summary>Throws with the site's own <c>error</c> where the answer is not a success.</summary>
    /// <exception cref="HttpRequestException">Where it is not, saying the status and why.</exception>
    public static async Task EnsureTaken(HttpResponseMessage answer, CancellationToken cancellation)
    {
        if (answer.IsSuccessStatusCode) return;

        string? why = null;

        try
        {
            why = (await answer.Content.ReadFromJsonAsync<Refusal>(Checks.Json, cancellation))?.Error;
        }
        catch (System.Text.Json.JsonException)
        {
        }
        catch (NotSupportedException)
        {
        }

        throw new HttpRequestException(
            $"the site answered {(int)answer.StatusCode} ({answer.ReasonPhrase}){(why is null ? "" : ": " + why)}",
            null,
            answer.StatusCode);
    }

    private sealed record Refusal(string? Error);
}
