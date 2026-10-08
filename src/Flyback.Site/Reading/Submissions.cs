using System.Text;
using System.Text.Json;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;

namespace Flyback.Site.Reading;

internal static class Submissions
{
    public const int NameLimit = 60;

    private const char ByteOrderMark = (char)0xFEFF;

    /// <summary>The most a submitted bundle may unpack to: a few times the upload limit, where a sound or a picture barely compresses.</summary>
    public const long BundleLimit = 128L << 20;

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
            ".fbk" or PatchBundle.Extension => Read(fileName, file),
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
        { Length: > NameLimit } called => TextLimit.Clip(called, NameLimit).TrimEnd(),
        var called => called,
    };

    /// <summary>A patch only where the bytes are one: a bundle, or a document that is an object with a module in it.</summary>
    private static Patch? Read(string fileName, byte[] file)
    {
        try
        {
            if (!PatchFile.Bundled(fileName) && !Shaped(file)) return null;

            var patch = PatchFile.Read(fileName, file, limit: BundleLimit).Patch;

            return patch.Nodes.Count > 0 ? patch : null;
        }
        catch (Exception e) when (e is InvalidDataException or JsonException or IOException or DecoderFallbackException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether the text is an object with modules in it, asked before the reader
    /// fills in what any patch is short of: a document of anything else would read
    /// as an empty patch rather than be refused.
    /// </summary>
    private static bool Shaped(byte[] file)
    {
        using var document = JsonDocument.Parse(new UTF8Encoding(false, true).GetString(file).TrimStart(ByteOrderMark));

        return document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty(nameof(Patch.Nodes), out var nodes)
            && nodes.ValueKind == JsonValueKind.Array
            && nodes.GetArrayLength() > 0;
    }
}
