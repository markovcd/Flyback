using Flyback.App.Bars;
using Flyback.App.Capture;
using Flyback.App.Canvas;
using Flyback.App.Gallery;
using Flyback.App.Knobs;
using Flyback.App.Statistics;

namespace Flyback.App;

/// <summary>Connects playback changes to the controls and usage display that follow them.</summary>
internal sealed class PlaybackControls(
    Playback playback,
    ShellLayout shell,
    PanelKnobs knobs,
    TakeRecording recording,
    TransportControls transport,
    PatchFiles files,
    NodeEditor editor,
    Usage usage,
    PresetSlot presets)
{
    /// <summary>Subscribes the window's playback consumers to their shared hubs.</summary>
    public void Wire()
    {
        playback.Compiled += (_, _) =>
        {
            shell.ShowPreview(playback.HasPicture);
            knobs.Refresh();
            recording.Mark();
        };

        playback.TransportChanged += (_, _) => transport.Sync();

        // A patch saved somewhere new reads what it names from there.
        files.Moved += (_, _) => playback.Recompile(files.Sounds, files.Pictures, () => recording.Running);

        // Count what is playing, once it starts rather than on every compile.
        playback.Started += (_, _) => usage.Played(
            editor.History.Patch.Nodes.Select(node => node.TypeId),
            editor.History.Patch.Connections.Count,
            presets.Showing?.Name);
    }
}
