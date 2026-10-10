using System.Text.Json.Nodes;
using Flyback.Editor.Canvas;
using Flyback.Editor.Files;
using Flyback.Editor.Gallery;
using Flyback.Editor.Keys;
using Flyback.Ui.Audio;
using Flyback.Ui.Controls;

namespace Flyback.Editor;

/// <summary>
/// What a script driving the editor reads instead of looking at it: the page's
/// <c>window.flyback.state()</c> and the Android editor's broadcast.
/// </summary>
internal sealed class EditorReadout(
    CanvasHistory history,
    PresetSlot presets,
    PreviewHost preview,
    IAudioEngine sound,
    ReportLine report,
    UnsavedWork unsaved,
    ScreenKeys keys)
{
    /// <summary>The patch on the canvas, what draws and plays it and how fast, and the last thing the editor said.</summary>
    public JsonObject Read()
    {
        var patch = history.Patch;
        var said = report.History;

        return new JsonObject
        {
            ["started"] = true,
            ["preset"] = presets.Showing?.Name,
            ["modules"] = patch.Nodes.Count,
            ["wires"] = patch.Connections.Count,
            ["backend"] = preview.Backend.ToString(),
            ["renderer"] = preview.Renderer,
            ["framesPerSecond"] = Math.Round(preview.FramesPerSecond, 1),
            ["frameMilliseconds"] = Math.Round(preview.FrameMilliseconds, 2),
            ["time"] = Math.Round(preview.Time, 2),
            ["soundBackend"] = sound.Backend,
            ["soundRunsOn"] = sound.RunsOn,
            ["soundSpeed"] = Math.Round(sound.Speed, 2),
            ["said"] = said.Count > 0 ? said[^1] : null,
            ["unsaved"] = unsaved.MustAsk,
            ["keys"] = keys.Shown,
        };
    }
}
