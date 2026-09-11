using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Flyback.Core;

namespace Flyback.App.Assist;

/// <summary>
/// The conversations kept for patch files, in the application's own folder, one
/// file for each patch file that has one.
/// </summary>
/// <remarks>
/// Beside the document rather than in it: a <c>.fbk</c> is the patch and nothing
/// else (ADR-0020), where a bundle carries its conversation inside itself.
/// <para>
/// Found by the patch file's path, and believed only while that file is still
/// exactly the text it was saved as. A patch changed anywhere else — by another
/// program, a checkout, a copy dropped over it — is not the patch the
/// conversation was about, and carrying one on over a patch it never saw would
/// have the assistant describing modules that are not there.
/// </para>
/// </remarks>
/// <param name="folder">Somewhere other than the usual place, for the tests.</param>
public sealed class ConversationStore(string? folder = null)
{
    private readonly string root = folder ?? Folder;

    public static string Folder => Path.Combine(GlobalConstants.DataFolder, "sessions");

    /// <summary>
    /// Keeps <paramref name="conversation"/> for the patch file at
    /// <paramref name="path"/>, which has just been written as
    /// <paramref name="text"/> — or forgets whatever was kept for that file, when
    /// there is none.
    /// </summary>
    /// <remarks>
    /// Forgetting is not tidying. A file saved over with a patch that has no
    /// conversation must not open next time with the one the last patch had.
    /// </remarks>
    /// <returns>Whether that happened. Never throws: the patch is already on disk.</returns>
    public bool Keep(string path, string text, string? conversation)
    {
        try
        {
            var file = FileFor(path);

            if (conversation is null)
            {
                if (File.Exists(file)) File.Delete(file);

                return true;
            }

            Directory.CreateDirectory(root);

            var kept = new JsonObject
            {
                ["patch"] = Path.GetFullPath(path),
                ["fingerprint"] = Fingerprint(text),
                ["conversation"] = JsonNode.Parse(conversation),
            };

            File.WriteAllText(file, kept.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The conversation kept for the patch file at <paramref name="path"/>, or null
    /// where there is none — or where the file is no longer the
    /// <paramref name="text"/> it was saved as.
    /// </summary>
    public string? Find(string path, string text)
    {
        try
        {
            var file = FileFor(path);

            if (!File.Exists(file)) return null;
            if (JsonNode.Parse(File.ReadAllText(file)) is not JsonObject kept) return null;

            // The file is named by a hash of the path, and says the path inside
            // itself as well — which is the one a person looking in this folder
            // can read, and so the one that is checked.
            if (!string.Equals(Word(kept["patch"]), Normal(path), Comparison)) return null;
            if (Word(kept["fingerprint"]) != Fingerprint(text)) return null;

            return kept["conversation"]?.ToJsonString();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// How two paths are compared: without regard to case where the file system
    /// pays none, so the same file named two ways is found either way.
    /// </summary>
    private static StringComparison Comparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static string Normal(string path) => Path.GetFullPath(path);

    private string FileFor(string path)
    {
        var named = Normal(path);

        if (Comparison == StringComparison.OrdinalIgnoreCase) named = named.ToUpperInvariant();

        return Path.Combine(root, Hash(named)[..32] + ".json");
    }

    private static string Fingerprint(string text) => Hash(text);

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static string? Word(JsonNode? node) =>
        node?.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : null;
}
