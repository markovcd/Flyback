using Flyback.Core;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Flyback.Engine.Language;
using Flyback.Engine.Render;
using Flyback.Assist;

namespace Flyback.Cli.Models;

/// <summary>
/// The patch a conversation is about, and the file it is written back to with
/// the conversation, where the editor would look for both (ADR-0072).
/// </summary>
/// <param name="Opened">The patch and its files.</param>
/// <param name="Into">Where each answer is written.</param>
/// <param name="From">The file it was read from, or null for a preset or a new patch.</param>
/// <param name="Conversation">What was saved with it, as <see cref="SavedConversation.ToJson"/> wrote it.</param>
/// <param name="Carried">The bytes of a file the patch names, for packing a bundle.</param>
internal sealed record AskedPatch(
    Opened Opened,
    FileInfo Into,
    FileInfo? From,
    string? Conversation,
    Func<string, byte[]?> Carried)
{
    /// <summary>The extensions it writes.</summary>
    public static string Formats =>
        $".{PatchIO.FileExtension}, .{PatchLanguage.FileExtension} or {PatchBundle.Extension}";

    /// <summary>Whether <paramref name="file"/> is one this can write.</summary>
    public static bool Writable(FileInfo file) =>
        PatchFile.Bundled(file)
        || PatchFile.Sourced(file)
        || string.Equals(file.Extension, $".{PatchIO.FileExtension}", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Writes <paramref name="patch"/> and <paramref name="conversation"/> to
    /// <see cref="Into"/>. A loose file is left as it is when the patch did not
    /// change, so a turn that only talked does not reformat it.
    /// </summary>
    /// <returns>Why it could not be written, or null.</returns>
    public string? Write(Patch patch, bool changed, string conversation, ModuleCatalog modules, ConversationStore store)
    {
        try
        {
            if (PatchFile.Bundled(Into))
            {
                using var packed = new MemoryStream();

                PatchBundle.Write(packed, patch, Carried, modules, conversation);
                File.WriteAllBytes(Into.FullName, packed.ToArray());

                return null;
            }

            if (changed || !Into.Exists || !SameFile(Into, From))
            {
                File.WriteAllText(
                    Into.FullName,
                    PatchFile.Sourced(Into) ? PatchPrinter.Print(patch, modules) : PatchIO.ToJson(patch, modules));
            }

            Into.Refresh();

            return store.Keep(Into.FullName, File.ReadAllText(Into.FullName), conversation)
                ? null
                : $"wrote {Into.Name}, but the conversation about it could not be kept with it.";
        }
        catch (Exception ex)
        {
            return $"{Into.Name}: {ex.Message}";
        }
    }

    private static bool SameFile(FileInfo one, FileInfo? other) =>
        other is not null && string.Equals(one.FullName, other.FullName, StringComparison.OrdinalIgnoreCase);

    /// <summary>A name for messages: the file it came from, or where it is going.</summary>
    public string Name => (From ?? Into).Name;

    /// <summary>Says <paramref name="problem"/> the way every command does.</summary>
    public static string Complaint(string problem) => $"{GlobalConstants.ApplicationName}: {problem}";

    /// <summary>What a patch read from <paramref name="from"/> names, off the disk beside it.</summary>
    public static Func<string, byte[]?> Beside(FileInfo from)
    {
        var folder = from.DirectoryName ?? ".";

        return path => PatchPaths.Carriable(path, folder);
    }

    /// <summary>What a bundle carries, falling back to the disk beside it.</summary>
    public static Func<string, byte[]?> Within(BundleFiles files, FileInfo from)
    {
        var beside = Beside(from);

        return path => files.Bytes.GetValueOrDefault(path) ?? beside(path);
    }
}
