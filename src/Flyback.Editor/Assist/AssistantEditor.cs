using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Editor.Canvas;
using Flyback.Editor.Hubs;
using Flyback.Host;
using Flyback.Plugins.Hosting;
using Flyback.Ui.Controls;

namespace Flyback.Editor.Assist;

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

    public string? Text => document.AssistantText;

    public void Apply(Patch patch, string? text = null)
    {
        document.TakeFromAssistant(patch, text);
        preview.Rewind();
    }

    public void Report(string message, string? detail) => report.Say(message, detail);

    public ISampleLibrary Samples => files.Sounds;

    public IImageLibrary Pictures => files.Pictures;

    public IReadOnlyList<PatchPreset> Presets() => PresetLibrary.Ordered(plugins.Presets, presets);
}
