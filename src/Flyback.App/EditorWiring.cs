using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
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
using Flyback.App.Settings;
using Flyback.Core.Compile;

namespace Flyback.App;

/// <summary>Connects events between editor services without making them depend on each other.</summary>
internal sealed class EditorWiring(
    PluginInstalls pluginInstalls,
    PatchOpening patchOpening,
    UnsavedWork unsaved,
    TakeRecording recording,
    PatchFiles files,
    PanelKnobs knobs,
    PresetSlot presets,
    Toolbar toolbar,
    TransportControls transport,
    Playback playback,
    SettingsSession settingsSession,
    IDialog dialog,
    IlCompiler compiler,
    OutputSections outputSections,
    ShellLayout shell,
    MidiHub midi,
    PreviewHost preview,
    ReportLine report,
    Usage usage,
    NodeEditor editor,
    Inspector inspector,
    Document document,
    SourceView source,
    CanvasSection canvasSection)
{
    private bool wired;

    public void Wire(Action refreshEditState, Action showOwnership)
    {
        if (wired) return;
        wired = true;

        toolbar.Open.Click += async (_, _) => await patchOpening.PickAndOpenAsync();
        toolbar.Save.Click += async (_, _) => await unsaved.SavePatchAsync();
        toolbar.Undo.Click += (_, _) => document.Undo();
        toolbar.Redo.Click += (_, _) => document.Redo();
        toolbar.Tidied += (_, onlySelected) => document.Tidy(onlySelected);
        toolbar.Swap.IsCheckedChanged += (_, _) => shell.SwapPreview(toolbar.Swap.IsChecked == true);
        toolbar.Pause.Click += (_, _) => transport.TogglePause();
        toolbar.Rewind.Click += (_, _) => playback.Rewind();
        toolbar.Record.Click += async (_, _) => await recording.ToggleAsync();
        toolbar.Assistant.IsCheckedChanged += (_, _) => shell.ShowAssistant(toolbar.Assistant.IsChecked == true);
        toolbar.Settings.Click += async (_, _) => await settingsSession.ShowAsync();
        toolbar.Plugins.Click += async (_, _) => await pluginInstalls.ShowAsync();
        toolbar.About.Click += async (_, _) => await dialog.Show("About", About.View());

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
        toolbar.Knobs.IsCheckedChanged += (_, _) => shell.ShowControls(toolbar.Knobs.IsChecked == true);
        knobs.Wanted += (_, _) => shell.ShowControls(true);

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
        editor.History.HistoryChanged += (_, _) => refreshEditState();
        editor.Gestures.GestureFinished += (_, _) =>
        {
            if (shell.PreviewHideWaiting) shell.ShowPreview(playback.HasPicture);
        };
        editor.Dial.InputTurned += (_, pick) => document.Turned(pick.Node, pick.Port);
        editor.Dial.InputLetGo += (_, pick) =>
        {
            document.HandCameOff();
            if (editor.Selection.Focused?.Id == pick.Node || editor.Selection.Group?.Members.Contains(pick.Node) == true) inspector.Build();
        };

        document.EditStateChanged += (_, _) => refreshEditState();
        document.PanelStale += (_, _) => inspector.Build();
        document.OwnershipChanged += (_, _) => showOwnership();
        document.ViewChanged += (_, _) =>
        {
            if (toolbar.Code.IsChecked != document.ShowingCode) toolbar.Code.IsChecked = document.ShowingCode;
        };
        toolbar.Code.IsCheckedChanged += (_, _) => document.ShowCode(toolbar.Code.IsChecked == true);

        source.EditorFontSize = canvasSection.EditorFontSize;
        source.EditorFontSizeChanged += (_, size) => canvasSection.SaveEditorFontSize(size);
        source.HandBackRequested += async (_, _) =>
        {
            if (document.Owned && await unsaved.MayLoseTheTextAsync()) document.HandBack();
        };

        inspector.Panel.AddHandler(
            InputElement.PointerReleasedEvent,
            (_, _) => document.HandCameOff(),
            RoutingStrategies.Bubble,
            handledEventsToo: true);
        inspector.Panel.AddHandler(InputElement.LostFocusEvent, (_, _) => document.HandCameOff(), RoutingStrategies.Bubble);
        inspector.Panel.AddHandler(
            InputElement.KeyUpEvent,
            (_, _) => document.HandCameOff(),
            RoutingStrategies.Bubble,
            handledEventsToo: true);
        inspector.Panel.AddHandler(
            InputElement.PointerWheelChangedEvent,
            (_, _) => document.HandCameOff(),
            RoutingStrategies.Bubble,
            handledEventsToo: true);

        midi.Played += preview.Refresh;
        midi.Trouble += message => report.Say(message);
        midi.Heard += () => usage.Count(Used.Instrument);

        compiler.Failed += message => Dispatcher.UIThread.Post(() => report.Say(message));
        var gpu = outputSections.Gpu;
        preview.BackendChanged += message =>
        {
            // Keep the selected request visible when the renderer falls back.
            gpu.SelectedIndex = preview.Wanted == PreviewBackend.Gpu ? 0 : 1;
            gpu.IsEnabled = preview.GpuAvailable;
            ToolTip.SetTip(gpu, preview.GpuAvailable ? OutputSections.GpuTip : message);
            report.Say(message);
        };

        editor.Report.Said += (_, message) => report.Say(message);
        report.Said += (_, message) => Trace.WriteLine($"{DateTime.Now:HH:mm:ss}  {message}");
    }
}
