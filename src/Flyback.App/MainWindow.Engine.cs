using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Flyback.App.Audio;
using Flyback.App.Controls;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins.Audio;
using Flyback.Plugins.Hosting;

namespace Flyback.App;

/// <summary>
/// The join between the window and the instrument: opening a sound device, turning an
/// edited patch back into two programs, and saying what came of either. Everything
/// the status bar carries originates here.
/// </summary>
/// <remarks>
/// A patch is recompiled whole on every edit, which keeps this to one handler and no
/// invalidation to get wrong. The device comes from a plugin, so nothing here knows
/// what a backend is called.
/// </remarks>
[SuppressMessage("Design", "CA1001", Justification = "Torn down in OnClosed; a window is closed, not disposed.")]
public sealed partial class MainWindow
{
    /// <summary>
    /// Puts the Sound settings just saved in force: a device made from them takes
    /// the old one's place, and the sound carries on through it where it was.
    /// </summary>
    /// <remarks>
    /// Only the device is new. The engine, its program and the clock the preview
    /// follows all stay, which is why this can happen on Save rather than at the next
    /// launch (ADR-0085). A backend's <see cref="IAudioOutput.Create"/> opens nothing,
    /// so a device that is busy or gone says so when it is started — through
    /// <see cref="SetAudioEnabled"/>, the same as at launch.
    /// </remarks>
    private void ReopenAudio()
    {
        var next = Sound.Open(plugins, outputSettings);

        if (next.Failure is { } failure)
        {
            next.Device.Dispose();
            Report($"Could not open sound: {failure}", PluginSummary.Text(plugins, sound.Failure, assistant?.Summary));
            return;
        }

        if (audio.IsRunning) SetAudioEnabled(false);

        if (!audio.Use(next.Device))
        {
            next.Device.Dispose();
            Report("That device plays at another rate, so it is used from the next time Flyback starts.");
            SyncAudioToVolume();
            return;
        }

        sound = next;
        audioBlocked = false;

        SyncAudioToVolume();
    }

    /// <summary>
    /// The chart the picture is rooted at: the selected module, when that is a Probe,
    /// a Scope or an Analyzer.
    /// </summary>
    /// <remarks>
    /// Selection rather than a mode, because a chart is something you look at rather
    /// than something a patch is left in — clicking away puts the picture back, and
    /// nothing about the patch changes. The speakers root at the Output whatever the
    /// screen is doing.
    /// </remarks>
    private NodeInstance? Probed =>
        editor.SelectedNode is { } selected && NodeCatalog.IsChart(selected.TypeId) ? selected : null;

    /// <summary>
    /// Whether there is anything for the preview to show. A chart rooted at a Probe
    /// is a picture like any other, whatever the Output's own 'color' says.
    /// </summary>
    private bool HasPicture => Probed is not null || editor.Patch.Reaches().Picture;

    /// <summary>Which probe the picture was last compiled for, or null for the patch itself.</summary>
    private Guid? showingProbe;

    /// <summary>
    /// Selecting a Probe is what puts its chart on the screen and selecting
    /// anything else is what takes it off again. No other selection changes the
    /// picture, so this recompiles only when that one does.
    /// </summary>
    private void ProbeSelectionChanged()
    {
        if (Probed?.Id != showingProbe) Recompile();
    }

    /// <summary>
    /// One patch, one program per sink. The audio program is compiled even when
    /// sound is off, so switching it on is instant and the status line can show
    /// what the ear would cost.
    /// </summary>
    private void Recompile()
    {
        var probe = Probed;
        showingProbe = probe?.Id;

        var result = probe is null
            ? editor.Patch.CompileForVideo(samples: Sounds, pictures: Pictures, played: true)
            : editor.Patch.CompileForProbe(probe.Id, samples: Sounds, pictures: Pictures, played: true);

        preview.Program = result.Program;
        if (preview.Backend == PreviewBackend.Cpu) compiler.Submit(result.Program, IlLane.Picture);

        audio.Update(editor.Patch, Sounds);

        ShowPreview(HasPicture);

        // Both programs are new, so both of their blocks are, and whatever is
        // being held has to be written into them before the next frame or the
        // next buffer. Turning a knob while playing a note recompiles the patch,
        // and the note must not be cut off by the edit.
        preview.Live = new LiveValues(result.Program.LiveInputs);
        var relaid = midi.Lay(editor.Patch.KeyboardScale);
        midi.Follow(preview.Live, audio.Live);
        RefreshControls();

        // What the ear reaches is said too. Compiling backwards from one sink
        // means the video pass never visits a module only the speakers reach —
        // and stops at the first line when there is no screen at all — so a
        // patch built for sound had nothing said about it, however wrong it was.
        var said = result.Issues
            .Concat(editor.Patch.CompileForAudio(samples: Sounds).Issues)
            .Select(i => i.Message)
            .Distinct();

        // That the screen is showing a chart rather than the patch is said here
        // and nowhere else. Without it a probe left selected looks exactly like
        // a patch that has stopped working.
        if (probe is not null)
        {
            said = said.Prepend(probe.TypeId switch
            {
                NodeCatalog.ScopeTypeId =>
                    "Showing the Scope — it charts what the speakers played, so turn the Output's "
                    + "Volume up to see anything. Select another module for the picture.",
                NodeCatalog.AnalyzerTypeId =>
                    "Showing the Analyzer — it charts the spectrum of what the speakers played, so "
                    + "turn the Output's Volume up to see anything. Select another module for the picture.",
                _ => "Showing the Probe — select another module for the picture.",
            });
        }

        // Said when it changes, since nothing on the canvas shows it: a patch
        // opened on a scale plays differently from the keys under your hands
        // before anything is selected.
        if (relaid) said = said.Prepend(midi.Keyboard.Described);

        // Each of them, rather than one sentence with bullets between: they are
        // separate problems, they arrive and are fixed separately, and the log
        // behind the line gives each its own row. The bar joins them back up,
        // because there is only one line to say them on.
        Report(said.ToList());

        MarkRecordable();
        SyncAudioToVolume();
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
    private void Report(string message, string? detail = null, bool progress = false) =>
        report.Say(message, detail, progress);

    /// <summary>
    /// The same, for everything a compile found at once. Each is a line of its
    /// own in the log; the bar joins them, having only the one line.
    /// </summary>
    private void Report(IReadOnlyList<string> messages) => report.Say(messages);

    /// <summary>
    /// Brings the audio device into line with what Volume now says, turning it on
    /// exactly where the toggle this replaced would have been clicked on, and off
    /// where it would have been clicked off — ADR-0079. Called after every
    /// recompile, so a drag through nought is caught the moment it crosses rather
    /// than on release.
    /// </summary>
    /// <remarks>
    /// Guarded on <see cref="AudioEngine.IsRunning"/> rather than called
    /// unconditionally: <see cref="SetAudioEnabled"/> opens or closes a real
    /// device, and a recompile happens on every knob frame while a slider is
    /// dragged (ADR-0021).
    /// </remarks>
    private void SyncAudioToVolume()
    {
        SyncTransport();

        var wanted = !paused && Audible;

        // Never off in the middle of a take. One with sound in it is paced by the
        // samples it is handed, so a device stopped under it stops the file —
        // picture and all — at that instant, and fading Volume to nought is how
        // a take is ended. It records the silence instead, and the device is
        // asked about again when the take is over.
        if (!wanted && recorder is not null) return;

        // Nor while a preset from the gallery is being heard through it, which
        // is what started it if the patch had not.
        if (!wanted && audio.IsAuditioning) return;

        if (wanted != audio.IsRunning) SetAudioEnabled(wanted);
    }

    private void SetAudioEnabled(bool enabled)
    {
        if (enabled)
        {
            // Not audio.Update — Recompile, the only caller that reaches here,
            // has already handed the engine a program built from Sounds, and a
            // second one built without it would undo that with the wrong one.
            try
            {
                audio.Start();
            }
            catch (Exception ex)
            {
                // A device is only really opened here, so this is where a card
                // that is busy, unplugged or missing its library says so. Blocked
                // rather than retried: whatever it is will not have fixed itself
                // by the next edit, and ADR-0025 promises that nothing a plugin
                // does takes the shell down.
                Report($"Sound could not start — {ex.Message}", PluginSummary.Text(plugins, sound.Failure, assistant?.Summary));
                audioBlocked = true;
                return;
            }

            // The patch is playing, which is the moment what is in it is worth
            // counting (ADR-0094). Here rather than at a compile: a patch is
            // recompiled on every knob frame, and what it is made of is only
            // interesting where somebody is listening to it.
            usage.Played(
                editor.Patch.Nodes.Select(node => node.TypeId),
                editor.Patch.Connections.Count,
                OrderedPresets().ElementAtOrDefault(presetShowing)?.Name);

            // Sound cannot stretch, so it leads and the picture follows — and
            // the same tick is where the picture is told what the speakers have
            // just played: a Scope's chart refilled, and a Meter's reading put
            // where the frame will read it. Here rather than in the renderer
            // because this is the one moment in the loop when the two paths are
            // both stopped: the callback is not mid-buffer as far as anything
            // here can tell, and the frame has not started. It is also the exact
            // scope of the promise both modules make — no clock, no sound, and
            // nothing new to hear.
            preview.Clock = () =>
            {
                audio.Listen(preview.Program, preview.Live);
                return audio.Time;
            };
        }
        else
        {
            preview.Clock = paused ? () => frozenAt : null;
            audio.Stop();

            // The picture goes on being drawn with nothing playing, so every
            // Meter has to be told that rather than left holding its last
            // reading. A Scope is left, which is the difference between a chart
            // of the past and a measurement of now.
            audio.Deafen(preview.Live);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        // Before the device goes, and before anything else: a take whose header
        // was never patched is not a file, so a window closed mid-recording
        // waits here for it rather than abandoning it. OnClosing has normally
        // dealt with it already, and this is for the close that could not be
        // put off.
        FinishTakeNow();

        // Whatever there was to lose has been asked about by now, and answered.
        StopRecovery();

        audio.Dispose();
        compiler.Dispose();

        // And the instruments, which are hardware somebody else may want back. A
        // port left open outlives the window that was reading it.
        midi.Dispose();

        base.OnClosed(e);
    }

    private void UpdateStatus()
    {
        var nodes = editor.Patch.Nodes.Count;
        var wires = editor.Patch.Connections.Count;
        var ops = preview.Program.Ops.Length;

        // Which renderer produced the rate is part of what it means, so it is
        // said alongside — what is actually drawing, not what was asked for.
        var backend = preview.Backend == PreviewBackend.Gpu ? "GPU" : "CPU";

        // Only while the window is somebody's: a window behind others is drawn
        // at whatever rate the system leaves it, which says nothing about Flyback.
        if (IsActive) usage.Drew(preview.FramesPerSecond, preview.Backend == PreviewBackend.Gpu);

        status.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{nodes} modules · {wires} wires · {ops} ops   |   t = {StatusClock.Text(preview.Time)}   |   {preview.FramesPerSecond:0} fps   |   {backend}");
    }
}
