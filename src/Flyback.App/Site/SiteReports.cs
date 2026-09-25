using System.Net.Http.Json;

namespace Flyback.App.Site;

/// <summary>Reporting a shared preset or plugin to the preset site's admin.</summary>
internal static class SiteReports
{
    /// <summary>Why something may be reported, as the site takes them, with what each says.</summary>
    public static readonly IReadOnlyList<(string Reason, string Said)> Reasons =
    [
        ("broken", "It does not open or does not work"),
        ("harmful", "It is harmful, or does something it does not say"),
        ("offensive", "It is offensive"),
        ("stolen", "It is somebody else's work"),
        ("other", "Something else"),
    ];

    public const int DetailsLimit = 1000;

    /// <summary>
    /// Posts the report about the <paramref name="kind"/> with <paramref name="id"/>.
    /// Throws where the site cannot be reached or does not take it.
    /// </summary>
    public static async Task SendAsync(HttpClient http, Uri root, string kind, string id, string reason, string? details, CancellationToken cancel)
    {
        var at = new Uri(root, $"api/v1/{kind}s/{Uri.EscapeDataString(id)}/reports");

        using var response = await http.PostAsJsonAsync(at, new { reason, details }, cancel);

        response.EnsureSuccessStatusCode();
    }
}
