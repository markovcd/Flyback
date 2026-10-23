using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Editor.Assist;

namespace Flyback.Editor.Tests.Assist;

/// <summary>A canvas holding one patch, counting what was put on it.</summary>
internal sealed class FakeAssistantEditor(Patch current) : IAssistantEditor
{
    public Patch Current { get; set; } = current;

    /// <summary>How many times a patch was put on the canvas.</summary>
    public int Applied { get; private set; }

    public List<string> Reported { get; } = [];

    /// <summary>The text the patch is written in, as a document the text owns would say.</summary>
    public string? Text { get; set; }

    public void Apply(Patch patch, string? text = null)
    {
        Current = patch;
        Text = text;
        Applied++;
    }

    public void Report(string message, string? detail) => Reported.Add(message);

    public ISampleLibrary? Samples => null;

    public IImageLibrary? Pictures => null;

    public IReadOnlyList<PatchPreset>? Presets() => null;
}
