using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Editor.Assist;

/// <summary>The editor as the assistant's column sees it: the patch it edits and where it says so.</summary>
public interface IAssistantEditor
{
    /// <summary>The patch on the canvas now.</summary>
    Patch Current { get; }

    /// <summary>
    /// The text the patch is written in, where the text owns it and nothing typed is
    /// waiting to be applied; null where a printing stands for it.
    /// </summary>
    string? Text { get; }

    /// <summary>Puts the assistant's patch on the canvas as one edit, which undoes like any other.</summary>
    /// <param name="patch"></param>
    /// <param name="text">The text the assistant left it in, which replaces the document's where the text owns it.</param>
    void Apply(Patch patch, string? text = null);

    /// <summary>Says <paramref name="message"/> on the status line.</summary>
    void Report(string message, string? detail);

    /// <summary>Where the patch's sounds are looked for.</summary>
    ISampleLibrary? Samples { get; }

    /// <summary>Where the patch's pictures are looked for.</summary>
    IImageLibrary? Pictures { get; }

    /// <summary>The presets a conversation may read for ideas, or null for the ones the plugins offer.</summary>
    IReadOnlyList<PatchPreset>? Presets();
}
