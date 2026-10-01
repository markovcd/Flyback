using System.Text.Json;
using System.Text.Json.Nodes;

namespace Flyback.Core;

/// <summary>
/// <c>settings.json</c>: every program's settings in one file, a section a concern,
/// each read and written on its own.
/// </summary>
/// <remarks>
/// A section that is missing or does not parse reads as nothing, so the rest of the
/// file still loads. Writing a section keeps the others as they are, and a file that
/// does not parse is replaced by one holding only the section written.
/// </remarks>
internal static class SettingsFile
{
    /// <summary>Where the programs keep their settings.</summary>
    public static string Path => System.IO.Path.Combine(GlobalConstants.DataFolder, "settings.json");

    /// <summary>One writer at a time, since every write rewrites the whole file.</summary>
    private static readonly Lock Gate = new();

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>The section named <paramref name="section"/> as JSON text, or null. Never throws.</summary>
    public static string? Read(string path, string section)
    {
        try
        {
            lock (Gate)
                return File.Exists(path) && Whole(File.ReadAllText(path))?[section] is { } node
                    ? node.ToJsonString()
                    : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Puts <paramref name="json"/> in as the section named <paramref name="section"/>. Throws if it cannot write, so the caller can say so.</summary>
    public static void Write(string path, string section, string json)
    {
        lock (Gate)
        {
            var whole = (File.Exists(path) ? Whole(File.ReadAllText(path)) : null) ?? [];

            whole[section] = JsonNode.Parse(json);

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path) ?? GlobalConstants.DataFolder);

            // Written beside and moved over, so a write that fails halfway leaves the old file whole.
            var partial = path + ".partial";

            File.WriteAllText(partial, whole.ToJsonString(Indented));
            File.Move(partial, path, overwrite: true);
        }
    }

    /// <summary>The file's sections, or null for text that is not a JSON object.</summary>
    private static JsonObject? Whole(string text)
    {
        try
        {
            return JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
