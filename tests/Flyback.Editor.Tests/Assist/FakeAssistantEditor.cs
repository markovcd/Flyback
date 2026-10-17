using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Editor.Assist;
using Flyback.Engine.Graph;

namespace Flyback.Editor.Tests.Assist;

/// <summary>A canvas holding one patch, counting what was put on it.</summary>
internal sealed class FakeAssistantEditor(Patch current) : IAssistantEditor
{
    public Patch Current { get; set; } = current;

    /// <summary>How many times a patch was put on the canvas.</summary>
    public int Applied { get; private set; }

    public List<string> Reported { get; } = [];

    public void Apply(Patch patch)
    {
        Current = patch;
        Applied++;
    }

    public void Report(string message, string? detail) => Reported.Add(message);

    public ISampleLibrary? Samples => null;

    public IImageLibrary? Pictures => null;

    public IReadOnlyList<PatchPreset>? Presets() => null;
}
