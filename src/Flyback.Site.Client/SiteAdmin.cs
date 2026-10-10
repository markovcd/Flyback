namespace Flyback.Site.Client;

/// <summary>
/// A client for the preset site's admin API, carrying the Cloudflare Access service token
/// named by two environment variables. The token is put on each request and never printed.
/// </summary>
internal static class SiteAdmin
{
    public const string IdVariable = "FLYBACK_ACCESS_ID";

    public const string SecretVariable = "FLYBACK_ACCESS_SECRET";

    /// <summary>The client, or null with what is missing.</summary>
    public static HttpClient? Client(string server, Func<string, string?> environment, out string? problem)
    {
        if (SiteAddress.Parse(server, out problem) is not { } address) return null;

        var id = environment(IdVariable);
        var secret = environment(SecretVariable);

        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(secret))
        {
            problem = $"set {IdVariable} and {SecretVariable} to the site's Access service token.";
            return null;
        }

        var client = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.Add("CF-Access-Client-Id", id);
        client.DefaultRequestHeaders.Add("CF-Access-Client-Secret", secret);

        return client;
    }
}
