using Flyback.App.Bars;
using Flyback.App.Capture;
using Flyback.App.Canvas;
using Flyback.App.Controls;
using Flyback.App.Files;
using Flyback.App.Gallery;
using Flyback.App.Inspect;
using Flyback.App.Knobs;
using Flyback.App.Midi;
using Flyback.App.PluginPackages;
using Flyback.App.Statistics;

namespace Flyback.App;

/// <summary>Connects events between editor services without making them depend on each other.</summary>
internal sealed class EditorWiring(
    PluginInstalls pluginInstalls,
    UnsavedWork unsaved,
    TakeRecording recording,
    PatchFiles files,
    PanelKnobs knobs,
    PresetSlot presets,
    TransportControls transport,
    Playback playback,
    ShellLayout shell,
    MidiHub midi,
    PreviewHost preview,
    ReportLine report,
    Usage usage,
    NodeEditor editor,
    Inspector inspector,
    Document document)
{
    private bool wired;

    public void Wire()
    {
        if (wired) return;
        wired = true;

        pluginInstalls.RestartRequested += async (_, request) =>
        {
            try
            {
                request.Complete(await unsaved.RelaunchAsync(request.Reopen, recording.InHand));
            }
            catch (Exception ex)
            {
                request.Fail(ex);
            }
        };

        files.Arrived += (_, _) => knobs.Hub.Forget();
        files.Saved += (_, _) => presets.Clear();
        files.Moved += (_, _) => playback.Recompile(files.Sounds, files.Pictures, () => recording.Running);
        recording.Marked += (_, _) => transport.Sync();
        // A capture cannot continue after its picture disappears.
        preview.CaptureLost += recording.Stop;

        playback.Compiled += (_, _) =>
        {
            shell.ShowPreview(playback.HasPicture);
            knobs.Refresh();
            recording.Mark();
        };
        playback.TransportChanged += (_, _) => transport.Sync();
        playback.Started += (_, _) => usage.Played(
            editor.History.Patch.Nodes.Select(node => node.TypeId),
            editor.History.Patch.Connections.Count,
            presets.Showing?.Name);

        editor.History.PatchChanged += (_, _) =>
        {
            playback.Recompile(files.Sounds, files.Pictures, () => recording.Running, opened: editor.History.Opening);
            inspector.Sync();
        };
        editor.Selection.Changed += (_, _) =>
        {
            inspector.Build();
            playback.ProbeSelectionChanged(files.Sounds, files.Pictures, () => recording.Running);
        };
        editor.Dial.InputTurned += (_, pick) => document.Turned(pick.Node, pick.Port);
        editor.Dial.InputLetGo += (_, pick) =>
        {
            document.HandCameOff();
            if (editor.Selection.Focused?.Id == pick.Node || editor.Selection.Group?.Members.Contains(pick.Node) == true) inspector.Build();
        };

        midi.Played += preview.Refresh;
        midi.Trouble += message => report.Say(message);
        midi.Heard += () => usage.Count(Used.Instrument);
    }
}
