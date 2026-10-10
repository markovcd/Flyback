namespace Flyback.Site.Client;

/// <summary>The preset site's address as <c>--server</c> gives it.</summary>
internal static class SiteAddress
{
    /// <summary>The address, ending in a slash so relative paths land under it, or null with why not.</summary>
    public static Uri? Parse(string server, out string? problem)
    {
        if (!Uri.TryCreate(server, UriKind.Absolute, out var address) || address.Scheme is not ("https" or "http"))
        {
            problem = $"--server {server}: give the site's whole address, e.g. https://presets.example.org/.";
            return null;
        }

        problem = null;
        return new Uri(address.AbsoluteUri.TrimEnd('/') + "/");
    }
}
