using Flyback.App.Controls;
using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.App;

/// <summary>The editor's side of the assistant's column.</summary>
internal sealed class AssistantEditor(
    NodeEditor editor,
    Document document,
    PreviewHost preview,
    ReportLine report,
    PatchFiles files,
    Lazy<PresetSlot> presets) : IAssistantEditor
{
    public Patch Current => editor.History.Patch;

    public void Apply(Patch patch)
    {
        document.TakeFromAssistant(patch);
        preview.Rewind();
    }

    public void Report(string message, string? detail) => report.Say(message, detail);

    public ISampleLibrary Samples => files.Sounds;

    public IImageLibrary Pictures => files.Pictures;

    public IReadOnlyList<PatchPreset> Presets() => presets.Value.Ordered();
}
