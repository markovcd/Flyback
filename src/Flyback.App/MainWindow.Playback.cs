using System.Diagnostics.CodeAnalysis;
using Flyback.App.Audio;
using Flyback.App.Controls;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins.Audio;
using Flyback.Plugins.Hosting;

namespace Flyback.App;

/// <summary>
/// What the window shows of <see cref="Playback"/>, and the one place anything is
/// said to the user.
/// </summary>
[SuppressMessage("Design", "CA1001", Justification = "Torn down in OnClosed; a window is closed, not disposed.")]
public sealed partial class MainWindow
{
    /// <summary>Called once, before anything compiles.</summary>
    private void WirePlayback()
    {
        playback.Compiled += (_, _) =>
        {
            ShowPreview(playback.HasPicture);
            knobs.Refresh();
            Recording.Mark();
        };

        playback.TransportChanged += (_, _) => SyncTransport();

        // A patch saved somewhere new reads what it names from there.
        files.Moved += (_, _) => playback.Recompile();

        // The patch is playing, which is the moment what is in it is worth
        // counting (ADR-0094). Not at a compile: a patch is recompiled on every
        // knob frame, and what it is made of is only interesting where somebody
        // is listening to it.
        playback.Started += (_, _) => usage.Played(
            editor.Patch.Nodes.Select(node => node.TypeId),
            editor.Patch.Connections.Count,
            presets.Showing?.Name);
    }

    /// <summary>
    /// The one place anything is said to the user. <paramref name="detail"/> is for
    /// what will not fit on a status bar — a list of missing plugins, say.
    /// </summary>
    /// <param name="detail"></param>
    /// <param name="progress">
    /// That this is the last message again with a new number in it, so the log keeps
    /// one entry for the run rather than one per update.
    /// </param>
    /// <param name="message"></param>
    internal void Report(string message, string? detail = null, bool progress = false) =>
        report.Say(message, detail, progress);

    /// <summary>
    /// The same, for everything a compile found at once. Each is a line of its
    /// own in the log; the bar joins them, having only the one line.
    /// </summary>
    private void Report(IReadOnlyList<string> messages) => report.Say(messages);

    protected override void OnClosed(EventArgs e)
    {
        // Before the device goes, and before anything else: a take whose header
        // was never patched is not a file, so a window closed mid-recording
        // waits here for it rather than abandoning it. OnClosing has normally
        // dealt with it already, and this is for the close that could not be
        // put off.
        Recording.FinishNow();

        // Whatever there was to lose has been asked about by now, and answered.
        keeper?.Stop();

        audio.Dispose();
        compiler.Dispose();

        // And the instruments, which are hardware somebody else may want back. A
        // port left open outlives the window that was reading it.
        midi.Dispose();

        base.OnClosed(e);
    }

}
