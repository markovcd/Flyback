using Avalonia.Controls;
using Flyback.Core.Render;

namespace Flyback.App.Capture;

/// <summary>What a take is called and which format it is written as.</summary>
internal static class Takes
{
    private const string DefaultName = "take";

    /// <summary>
    /// A patch's name as a file's. A preset is named for a person to read, and may
    /// hold what no file name can.
    /// </summary>
    internal static string FileNameFor(string? patchName)
    {
        if (string.IsNullOrWhiteSpace(patchName)) return DefaultName;

        var cleaned = string.Join("_", patchName.Split(Path.GetInvalidFileNameChars())).Trim(' ', '.');

        return cleaned.Length > 0 ? cleaned : DefaultName;
    }

    /// <summary>The format one of the two pickers is on.</summary>
    internal static ClipFormat Chosen(IReadOnlyList<ClipFormat> formats, ComboBox picker) =>
        formats[Math.Clamp(picker.SelectedIndex, 0, formats.Count - 1)];

    /// <summary>
    /// The format a file name asks for, given the two the settings are on.
    /// </summary>
    /// <remarks>
    /// An extension two formats share means the one chosen in the settings:
    /// <c>.mp4</c> is H.265 to somebody who picked H.265, and no name could say
    /// so otherwise. Any other extension means the first format that has it, and
    /// one nothing writes means the video format chosen.
    /// </remarks>
    internal static ClipFormat Format(string path, ClipFormat video, ClipFormat sound)
    {
        var extension = Path.GetExtension(path);

        foreach (var chosen in new[] { video, sound })
        {
            if (extension.Equals(chosen.Extension, StringComparison.OrdinalIgnoreCase)) return chosen;
        }

        return ClipFormats.ByExtension(path) ?? video;
    }
}
