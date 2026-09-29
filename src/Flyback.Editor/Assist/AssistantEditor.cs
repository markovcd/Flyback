using Flyback.App.Canvas;
using Flyback.App.Controls;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;

namespace Flyback.App.Assist;

/// <summary>The editor's side of the assistant's column.</summary>
internal sealed class AssistantEditor(
    NodeEditor editor,
    Document document,
    PreviewHost preview,
    ReportLine report,
    PatchFiles files,
    PluginCatalog plugins,
    PresetLibrary presets) : IAssistantEditor
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

    public IReadOnlyList<PatchPreset> Presets() => PresetLibrary.Ordered(plugins.Presets, presets);
}
