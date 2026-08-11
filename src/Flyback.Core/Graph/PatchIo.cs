using System.Text.Json;
using System.Text.Json.Serialization;

namespace Flyback.Core.Graph;

/// <summary>Reads and writes patches as JSON.</summary>
public static class PatchIo
{
    public const string FileExtension = "fbk";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string ToJson(Patch patch) => JsonSerializer.Serialize(patch, Options);

    public static Patch FromJson(string json) =>
        JsonSerializer.Deserialize<Patch>(json, Options) ?? new Patch();

    public static void Save(Patch patch, string path) => File.WriteAllText(path, ToJson(patch));

    public static Patch Load(string path) => FromJson(File.ReadAllText(path));
}
