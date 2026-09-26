using System.Text;
using System.Text.Json;
using Flyback.Core.Graph;

namespace Flyback.Server;

internal static class Submissions
{
    public const int NameLimit = 60;

    /// <summary>
    /// The preset in <paramref name="file"/>, or null where it is not a patch at all.
    /// </summary>
    /// <remarks>
    /// Unknown modules are fine: a patch built on a plugin is still a preset. The
    /// description, author and tags are the patch's own.
    /// </remarks>
    public static Submission? Read(string fileName, byte[] file, string? name)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        var patch = extension switch
        {
            ".fbk" => Loose(file),
            PatchBundle.Extension => Bundled(file),
            _ => null,
        };

        if (patch is null) return null;

        return new Submission(
            Named(name) ?? Named(Path.GetFileNameWithoutExtension(fileName)) ?? "Untitled",
            Patch.TidiedAuthor(patch.Author),
            Patch.Tidied(patch.Description),
            Patch.TidiedTags(patch.Tags) ?? [],
            Path.GetFileName(fileName),
            file);
    }

    /// <summary>A preset name held to one line and <see cref="NameLimit"/>, or null where it is blank.</summary>
    public static string? Named(string? name) => Patch.Tidied(name) switch
    {
        { Length: > NameLimit } called => called[..NameLimit].TrimEnd(),
        var called => called,
    };

    private static Patch? Loose(byte[] file)
    {
        string json;

        try
        {
            json = new UTF8Encoding(false, true).GetString(file);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }

        return Parsed(json);
    }

    private static Patch? Bundled(byte[] file)
    {
        try
        {
            using var archive = new MemoryStream(file, writable: false);
            var bundle = PatchBundle.Read(archive);

            return bundle.Patch.Nodes.Count > 0 ? bundle.Patch : null;
        }
        catch (Exception e) when (e is InvalidDataException or JsonException or IOException)
        {
            return null;
        }
    }

    /// <summary>A patch only where the text is one: an object with a module in it.</summary>
    private static Patch? Parsed(string json)
    {
        try
        {
            using (var document = JsonDocument.Parse(json))
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || !document.RootElement.TryGetProperty(nameof(Patch.Nodes), out var nodes)
                    || nodes.ValueKind != JsonValueKind.Array
                    || nodes.GetArrayLength() == 0)
                    return null;
            }

            return PatchIO.Read(json).Patch;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}