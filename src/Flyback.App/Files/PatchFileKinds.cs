using Avalonia.Platform.Storage;
using Flyback.Core;
using Flyback.Core.Graph;
using Flyback.Core.Language;
using Flyback.Core.Render;

namespace Flyback.App.Files;

/// <summary>The kinds of file the open, save and record pickers offer, and which kind a picked name is.</summary>
internal static class PatchFileKinds
{
    /// <summary>Whether a name the picker handed back is a bundle rather than a patch.</summary>
    internal static bool Bundled(string name) =>
        name.EndsWith(PatchBundle.Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a name the picker handed back is the patch written as text.</summary>
    internal static bool Sourced(string name) =>
        name.EndsWith($".{PatchLanguage.FileExtension}", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// What the save dialog offers, with whichever kind this document already is
    /// in front — a bundle or a text saved again should stay one without anybody
    /// having to type the extension.
    /// </summary>
    /// <param name="sourced">
    /// Whether the text is the document. It is then the one kind that loses
    /// nothing: a patch or a bundle written from it drops its comments, names and
    /// defs.
    /// </param>
    internal static IReadOnlyList<FilePickerFileType> SaveKinds(bool bundled, bool sourced = false) =>
        sourced ? [SourceFileType, PatchFileType, BundleFileType]
        : bundled ? [BundleFileType, PatchFileType, SourceFileType]
        : [PatchFileType, BundleFileType, SourceFileType];

    /// <summary>The extension a save offers, which is the first of <see cref="SaveKinds"/>.</summary>
    internal static string SaveExtension(bool bundled, bool sourced = false) =>
        sourced ? PatchLanguage.FileExtension
        : bundled ? PatchBundle.Extension[1..]
        : PatchIO.FileExtension;

    /// <summary>Everything the open dialog will read.</summary>
    internal static IReadOnlyList<FilePickerFileType> OpenKinds() =>
        [PatchFileType, BundleFileType, SourceFileType];

    /// <summary>One format as the file picker asks about it.</summary>
    private static FilePickerFileType Kind(ClipFormat format) =>
        new(format.Label) { Patterns = [$"*{format.Extension}"] };

    /// <summary>
    /// The kinds a recording could be written to.
    /// </summary>
    /// <remarks>
    /// No PNG, because a still is not a recording — ADR-0078 leaves stills to
    /// <c>flyback-cli render</c>. A patch that draws but makes no sound is still
    /// offered a video, which simply has no audio stream: it is a recording of
    /// everything the patch does, and a silent one is only wrong when there was
    /// sound to be had.
    /// <para>
    /// One kind each rather than every format there is. The two the settings
    /// window is set to are what this offers, since a picker listing nine
    /// extensions would be a second place to choose a format and a slower way to
    /// do it — and an extension typed over the suggestion is honored anyway
    /// (ADR-0089).
    /// </para>
    /// </remarks>
    /// <param name="video">
    /// The format a take with a picture is written as, defaulting to the one
    /// written here — which is also what a window with no settings file has.
    /// </param>
    /// <param name="sound">The format a take of the sound alone is written as.</param>
    internal static IReadOnlyList<FilePickerFileType> RecordKinds(
        Patch patch,
        ClipFormat? video = null,
        ClipFormat? sound = null)
    {
        var (picture, heard) = patch.Reaches();

        var movie = Kind(video ?? ClipFormats.MotionJpegAvi);
        var track = Kind(sound ?? ClipFormats.Wav);

        return (picture, heard) switch
        {
            (true, true) => [movie, track],
            (true, false) => [movie],
            (false, true) => [track],
            _ => [],
        };
    }

    private static FilePickerFileType PatchFileType => new($"{GlobalConstants.ApplicationName} patch")
    {
        Patterns = [$"*.{PatchIO.FileExtension}"],
    };

    /// <summary>
    /// A patch and everything it names, in one file — see
    /// <see cref="PatchBundle"/>. Offered beside the patch rather than instead
    /// of it: a bundle is what you send somebody, and a patch is what you work
    /// on.
    /// </summary>
    private static FilePickerFileType BundleFileType => new($"{GlobalConstants.ApplicationName} bundle")
    {
        Patterns = [$"*{PatchBundle.Extension}"],
    };

    /// <summary>
    /// The patch written in the language — text, and readable as text. Last of the
    /// three for a document the graph owns: a patch and a bundle are what that is
    /// saved as, and offering the lossy one first would put it where the habit lands.
    /// </summary>
    private static FilePickerFileType SourceFileType => new($"{GlobalConstants.ApplicationName} text")
    {
        Patterns = [$"*.{PatchLanguage.FileExtension}"],
    };
}
