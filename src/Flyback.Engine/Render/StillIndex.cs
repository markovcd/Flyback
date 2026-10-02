using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;

namespace Flyback.Engine.Render;

/// <summary>
/// Every preset a build offers and the still it drew of each, written by
/// <c>flyback-cli stills</c> beside the stills and read by the editor, the web editor
/// and the web viewer in place of drawing them (ADR-0163).
/// </summary>
/// <param name="Version">The build that drew them. A program trusts the index only when this is its own.</param>
public sealed record StillIndex(string Version, IReadOnlyList<StillEntry> Presets)
{
    public const string FileName = "index.json";

    /// <summary>The folder the stills are published in, beside the programs and at the root of a site.</summary>
    public const string Folder = "stills";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>What this build calls itself, which an index it drew carries.</summary>
    public static string ThisBuild { get; } =
        typeof(StillIndex).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";

    /// <summary>Whether this build drew the index, so its stills are of the presets this build has.</summary>
    public bool Current => Version == ThisBuild;

    /// <summary>The entry for the preset called <paramref name="name"/> and offered as <paramref name="kind"/>, or null.</summary>
    public StillEntry? Of(string name, PresetKind kind) =>
        Presets.FirstOrDefault(entry => entry.Name == name && entry.Kind == kind);

    public string Write() => JsonSerializer.Serialize(this, Options);

    /// <summary>The index in <paramref name="json"/>, or null where it is not one.</summary>
    public static StillIndex? Read(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<StillIndex>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
