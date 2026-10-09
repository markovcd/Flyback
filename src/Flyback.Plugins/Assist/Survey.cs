using System.Text.Json;

namespace Flyback.Plugins.Assist;

/// <summary>
/// What a survey found, on its way into and out of a provider's settings.
/// </summary>
/// <remarks>
/// One string under one key, because that is the only shape the settings file has
/// (ADR-0069): a list of models is not a string, so it is written as one here and read
/// back here. The file is hand-edited, so <see cref="Read"/> never throws and never
/// half-succeeds — anything it cannot make sense of is nothing at all.
/// </remarks>
public static class Survey
{
    /// <summary>
    /// What the survey is filed under. Stable — it is in the settings file of
    /// everybody who has ever run one.
    /// </summary>
    public const string Key = "models";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    /// <summary>The survey as the one string the settings file holds.</summary>
    internal static string Write(IEnumerable<ModelReport> found) =>
        JsonSerializer.Serialize(found.ToArray(), Options);

    /// <summary>Never throws, and never returns half a list.</summary>
    public static IReadOnlyList<ModelReport> Read(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return [];

        try
        {
            var found = JsonSerializer.Deserialize<ModelReport[]>(stored, Options);

            return found is null
                ? []
                : found.Where(m => m is not null && !string.IsNullOrWhiteSpace(m.Id)).ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
