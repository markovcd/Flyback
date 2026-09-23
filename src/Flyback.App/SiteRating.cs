using System.Globalization;
using System.Text.Json;

namespace Flyback.App;

/// <summary>How a shared preset or plugin is rated on the site, which is the only place it can be rated.</summary>
internal sealed record SiteRating(double Average, int Count)
{
    public const int Most = 5;

    public static readonly SiteRating None = new(0, 0);

    /// <summary>The average to the nearest whole star, nought where nobody has rated it.</summary>
    public int Stars => Count == 0 ? 0 : Math.Clamp((int)Math.Round(Average, MidpointRounding.AwayFromZero), 1, Most);

    public string Said => Count == 0
        ? "Not rated yet"
        : string.Create(CultureInfo.InvariantCulture, $"{Average:0.0} ({Count} {(Count == 1 ? "rating" : "ratings")})");

    /// <summary>The <c>rating</c> of a listed item, or <see cref="None"/> where it has none.</summary>
    public static SiteRating Read(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object
            || !item.TryGetProperty("rating", out var rating)
            || rating.ValueKind != JsonValueKind.Object
            || !rating.TryGetProperty("count", out var count) || count.ValueKind != JsonValueKind.Number || !count.TryGetInt32(out var many) || many <= 0
            || !rating.TryGetProperty("average", out var average) || average.ValueKind != JsonValueKind.Number || !average.TryGetDouble(out var mean))
            return None;

        return new SiteRating(Math.Clamp(mean, 1, Most), many);
    }
}
