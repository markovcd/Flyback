using Flyback.App.Audio;
using Flyback.App.Controls;
using Flyback.App.Midi;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins.Audio;
using Flyback.Plugins.Hosting;

namespace Flyback.App;

/// <summary>
/// The instrument itself: turning an edited patch back into two programs, the sound
/// device that plays one of them, and pausing or muting the pair (ADR-0148).
/// </summary>
/// <remarks>
/// A patch is recompiled whole on every edit, which keeps this to one handler and no
/// invalidation to get wrong. The device comes from a plugin, so nothing here knows
/// what a backend is called.
/// <para>
/// Paused is the same as in the viewer: the device stops and the picture is timed by a
/// clock that holds still, so an edit still redraws and a resume adds one frame rather
/// than the whole pause. Rewinding while paused stays paused, on the first frame.
/// </para>
/// </remarks>
internal sealed class Playback
{
    private readonly NodeEditor editor;
    private readonly PreviewHost preview;
    private readonly AudioEngine audio;
    private readonly IlCompiler compiler;
    private readonly MidiHub midi;
    private readonly ReportLine report;
    private readonly PluginCatalog plugins;
    private readonly Func<ISampleLibrary> sounds;
    private readonly Func<IImageLibrary> pictures;
    private readonly Func<bool> recording;
    private readonly Func<string?> assistantSummary;

    /// <summary>The patch has just been compiled, and the picture and the sound are playing it.</summary>
    public event EventHandler? Compiled;

    /// <summary>The device has just started playing the patch.</summary>
    public event EventHandler? Started;

    /// <summary>Paused, muted or audible may have changed.</summary>
    public event EventHandler? TransportChanged;

    /// <param name="recording">Whether a take is running, which the device may not be stopped under.</param>
    /// <param name="assistantSummary">What the assistant plugins came to, for a sound failure's detail.</param>
    public Playback(
        NodeEditor editor,
        PreviewHost preview,
        AudioEngine audio,
        IlCompiler compiler,
        MidiHub midi,
        ReportLine report,
        PluginCatalog plugins,
        AudioSetup sound,
        Func<ISampleLibrary> sounds,
        Func<IImageLibrary> pictures,
        Func<bool> recording,
        Func<string?> assistantSummary)
    {
        this.editor = editor;
        this.preview = preview;
        this.audio = audio;
        this.compiler = compiler;
        this.midi = midi;
        this.report = report;
        this.plugins = plugins;
        this.sounds = sounds;
        this.pictures = pictures;
        this.recording = recording;
        this.assistantSummary = assistantSummary;

        Sound = sound;
    }

    /// <summary>The sound backend and the device it opened.</summary>
    public AudioSetup Sound { get; private set; }

    /// <summary>
    /// Set once a device has refused to start, so a Volume left above nought does
    /// not retry it on every edit. Only a different device clears it — saved in the
    /// Sound settings, or found at the next launch — see ADR-0079 and ADR-0085.
    /// </summary>
    private bool blocked;

    /// <summary>Whether there is a device that has not refused to start.</summary>
    public bool CanSound => Sound.Output is not null && !blocked;

    /// <summary>Whether the speakers would be heard: there is a device, and the Output's Volume is up.</summary>
    public bool Audible => CanSound && Audio.Sound.VolumeIsUp(editor.Patch);

    public bool Paused { get; private set; }

    public bool Muted { get; private set; }

    /// <summary>Where the picture is held while <see cref="Paused"/>.</summary>
    private double frozenAt;

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
    public void ReopenAudio(OutputSettings settings)
    {
        var next = Audio.Sound.Open(plugins, settings);

        if (next.Failure is { } failure)
        {
            next.Device.Dispose();
            report.Say($"Could not open sound: {failure}", FailureDetail());
            return;
        }

        if (audio.IsRunning) SetAudioEnabled(false);

        if (!audio.Use(next.Device))
        {
            next.Device.Dispose();
            report.Say("That device plays at another rate, so it is used from the next time Flyback starts.");
            SyncAudioToVolume();
            return;
        }

        Sound = next;
        blocked = false;

        SyncAudioToVolume();
    }

    private string FailureDetail() => PluginSummary.Text(plugins, Sound.Failure, assistantSummary());

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
    public bool HasPicture => Probed is not null || editor.Patch.Reaches().Picture;

    /// <summary>Which probe the picture was last compiled for, or null for the patch itself.</summary>
    private Guid? showingProbe;

    /// <summary>The cue the patch last opened starts on. Edits made before it goes wait with it.</summary>
    private Cue? opening;

    /// <summary>Whether the patch last opened is still waiting to start.</summary>
    public bool Starting => opening?.Waiting == true;

    /// <summary>
    /// Selecting a Probe is what puts its chart on the screen and selecting
    /// anything else is what takes it off again. No other selection changes the
    /// picture, so this recompiles only when that one does.
    /// </summary>
    public void ProbeSelectionChanged()
    {
        if (Probed?.Id != showingProbe) Recompile();
    }

    /// <summary>
    /// One patch, one program per sink. The audio program is compiled even when
    /// sound is off, so switching it on is instant and the status line can show
    /// what the ear would cost.
    /// </summary>
    /// <param name="opened">
    /// A patch just opened, whose sound and picture start together once both are
    /// built: the sound's IL, and the picture's shader or IL.
    /// </param>
    public void Recompile(bool opened = false)
    {
        var probe = Probed;
        showingProbe = probe?.Id;

        // Held by this call until both programs have taken their parts.
        if (opened) opening = new Cue();
        else if (opening is { Waiting: true }) opening.Take();
        else opening = null;

        var start = opening;

        var samples = sounds();
        var images = pictures();

        var result = probe is null
            ? editor.Patch.CompileForVideo(samples: samples, pictures: images, played: true)
            : editor.Patch.CompileForProbe(probe.Id, samples: samples, pictures: images, played: true);

        // Not a picture that is never drawn: a hidden preview would hold the cue until it gave up.
        if (start is not null && HasPicture) result.Program.WaitFor(start);

        preview.Program = result.Program;
        if (preview.Backend == PreviewBackend.Cpu) compiler.Submit(result.Program, IlLane.Picture);

        audio.Update(editor.Patch, samples, start);
        start?.Give();

        // Both programs are new, so both of their blocks are, and whatever is
        // being held has to be written into them before the next frame or the
        // next buffer. Turning a knob while playing a note recompiles the patch,
        // and the note must not be cut off by the edit.
        preview.Live = new LiveValues(result.Program.LiveInputs);
        var relaid = midi.Lay(editor.Patch.KeyboardScale);
        midi.Follow(preview.Live, audio.Live);

        Compiled?.Invoke(this, EventArgs.Empty);

        // What the ear reaches is said too. Compiling backwards from one sink
        // means the video pass never visits a module only the speakers reach —
        // and stops at the first line when there is no screen at all — so a
        // patch built for sound had nothing said about it, however wrong it was.
        var said = result.Issues
            .Concat(editor.Patch.CompileForAudio(samples: samples).Issues)
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
        // behind the line gives each its own row.
        report.Say(said.ToList());

        SyncAudioToVolume();
    }

    public void Pause()
    {
        if (Paused) return;

        frozenAt = preview.Time;
        Paused = true;

        SetAudioEnabled(false);
        TransportChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Resume()
    {
        if (!Paused) return;

        Paused = false;

        // The device puts the audio clock back when it starts; with none, the picture runs on its own.
        preview.Clock = null;

        SyncAudioToVolume();
    }

    /// <summary>Silences the speakers without stopping the device, so the clock does not drift.</summary>
    public void ToggleMute()
    {
        Muted = !Muted;
        audio.Gain = Muted ? 0f : 1f;

        TransportChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Takes the picture and the sound back to zero seconds.</summary>
    public void Rewind()
    {
        audio.Rewind();
        preview.Rewind();

        // A paused clock reads the time it froze at, so the next tick would undo the rewind.
        if (!Paused) return;

        frozenAt = 0;
        preview.Time = 0;
    }

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
    public void SyncAudioToVolume()
    {
        TransportChanged?.Invoke(this, EventArgs.Empty);

        var wanted = !Paused && Audible;

        // Never off in the middle of a take. One with sound in it is paced by the
        // samples it is handed, so a device stopped under it stops the file —
        // picture and all — at that instant, and fading Volume to nought is how
        // a take is ended. It records the silence instead, and the device is
        // asked about again when the take is over.
        if (!wanted && recording()) return;

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
            // has already handed the engine a program built from the patch's
            // sounds, and a second one built without them would undo that.
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
                report.Say($"Sound could not start — {ex.Message}", FailureDetail());
                blocked = true;
                return;
            }

            Started?.Invoke(this, EventArgs.Empty);

            // Sound cannot stretch, so it leads and the picture follows — and
            // the same tick is where the picture is told what the speakers have
            // just played: a Scope's chart refilled, and a Meter's reading put
            // where the frame will read it. Here rather than in the renderer
            // because this is the one moment in the loop when the two paths are
            // both stopped. It is also the exact scope of the promise both
            // modules make — no clock, no sound, and nothing new to hear.
            preview.Clock = () =>
            {
                audio.Listen(preview.Program, preview.Live);
                return audio.Time;
            };
        }
        else
        {
            preview.Clock = Paused ? () => frozenAt : null;
            audio.Stop();

            // The picture goes on being drawn with nothing playing, so every
            // Meter has to be told that rather than left holding its last
            // reading. A Scope is left, which is the difference between a chart
            // of the past and a measurement of now.
            audio.Deafen(preview.Live);
        }
    }
}
