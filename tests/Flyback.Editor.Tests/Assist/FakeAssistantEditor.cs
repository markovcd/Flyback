using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Editor.Assist;
using Flyback.Engine.Language;

namespace Flyback.Editor.Tests.Assist;

/// <summary>A canvas holding one patch, counting what was put on it.</summary>
internal sealed class FakeAssistantEditor(Patch current) : IAssistantEditor
{
    public Patch Current { get; set; } = current;

    /// <summary>How many times a patch was put on the canvas.</summary>
    public int Applied { get; private set; }

    /// <summary>
    /// The text the patch is written in, as a document the text owns would say; null while
    /// the canvas owns the patch, which a proposal does not change.
    /// </summary>
    public string? Text { get; set; }

    /// <summary>The text the last proposal was handed over with.</summary>
    public string? Handed { get; private set; }

    public void Apply(Patch patch, string? text = null)
    {
        Current = patch;
        Handed = text;
        if (Text is not null) Text = text ?? PatchPrinter.Print(patch);
        Applied++;
    }

    public void Report(string message, string? detail) { }

    public ISampleLibrary? Samples => null;

    public IImageLibrary? Pictures => null;

    public IReadOnlyList<PatchPreset>? Presets() => null;
}
