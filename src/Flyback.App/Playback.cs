using Flyback.App.Assist;
using Flyback.App.Audio;
using Flyback.App.Canvas;
using Flyback.App.Capture;
using Flyback.App.Controls;
using Avalonia.Threading;
using Flyback.App.Midi;
using Flyback.App.Notices;
using Flyback.App.PluginPackages;
using Flyback.App.Statistics;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
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
/// Play, pause and rewind are the <see cref="Transport"/> the viewer uses too; what is
/// decided here is when the device runs.
/// </para>
/// </remarks>
internal sealed class Playback
    : IReactTo<PatchChanged>,
        IReactTo<SelectionChanged>,
        IReactTo<DocumentArrived>,
        IReactTo<FilesMoved>,
        IReactTo<RewindAsked>
{
    private readonly NodeEditor editor;
    private readonly AudioEngine audio;
    private readonly MidiHub midi;
    private readonly Transport transport;
    private readonly ReportLine report;
    private readonly PluginCatalog plugins;
    private readonly ChosenAssistant chosenAssistant;
    private readonly RecordingState recording;
    private readonly Reactions reactions;
    private readonly Usage usage;

    /// <summary>Where the patch's sound files are read from, as the document last said.</summary>
    private ISampleLibrary sounds = new SampleLibrary();

    /// <summary>Where the patch's pictures are read from, as the document last said.</summary>
    private IImageLibrary pictures = new ImageLibrary();

    /// <param name="recording">Whether a take is running, which the device may not be stopped under.</param>
    public Playback(
        NodeEditor editor,
        PluginCatalog plugins,
        ReportLine report,
        PreviewHost preview,
        AudioEngine audio,
        IlCompiler compiler,
        MidiHub midi,
        AudioSetup sound,
        ChosenAssistant chosenAssistant,
        RecordingState recording,
        Reactions reactions,
        Usage usage)
    {
        this.editor = editor;
        this.audio = audio;
        this.midi = midi;
        transport = new Transport(audio, preview, compiler, midi);
        this.report = report;
        this.plugins = plugins;
        this.chosenAssistant = chosenAssistant;
        this.recording = recording;
        this.reactions = reactions;
        this.usage = usage;

        Sound = sound;

        // The engine and the MIDI hub are the viewer's too, so they say what happens
        // through events rather than notices, and the instrument listens here.
        midi.Played += preview.Refresh;
        midi.Trouble += message => report.Say(message);
        compiler.Failed += message => Dispatcher.UIThread.Post(() => report.Say(message));
    }

    /// <summary>The programs are rebuilt before anything reads them.</summary>
    public int Priority => -10;

    public Task On(PatchChanged notice)
    {
        Recompile(opened: notice.Opened);
        return Task.CompletedTask;
    }

    public Task On(SelectionChanged notice)
    {
        ProbeSelectionChanged();
        return Task.CompletedTask;
    }

    public Task On(DocumentArrived notice)
    {
        (sounds, pictures) = (notice.Sounds, notice.Pictures);
        return Task.CompletedTask;
    }

    public Task On(FilesMoved notice)
    {
        (sounds, pictures) = (notice.Sounds, notice.Pictures);
        Recompile();
        return Task.CompletedTask;
    }

    public Task On(RewindAsked notice)
    {
        RewindPressed();
        return Task.CompletedTask;
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
    public bool Audible => CanSound && Audio.Sound.VolumeIsUp(editor.History.Patch);

    public bool Paused => transport.Paused;

    /// <summary>How many ops the sound's program runs for each sample.</summary>
    public int SoundOps => transport.SoundOps;

    /// <summary>How long the open patch plays for, in seconds.</summary>
    public double Length => editor.History.Patch.Lasts;

    public bool Muted => transport.Muted;

    /// <summary>Whether either running program reads the computer's keys, so a letter is a note.</summary>
    public bool Keyed => transport.Keyed;

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

    private string FailureDetail() => PluginSummary.Text(plugins, Sound.Failure, chosenAssistant.Summary);

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
        editor.Selection.Focused is { } selected && NodeCatalog.IsChart(selected.TypeId) ? selected : null;

    /// <summary>
    /// Whether there is anything for the preview to show. A chart rooted at a Probe
    /// is a picture like any other, whatever the Output's own 'color' says.
    /// </summary>
    public bool HasPicture => Probed is not null || editor.History.Patch.Reaches().Picture;

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
        var (samples, images) = (sounds, pictures);
        var probe = Probed;
        showingProbe = probe?.Id;

        // Held by this call until both programs have taken their parts.
        if (opened)
        {
            opening = new Cue();
            reactions.Raise(new PatchStarting());
        }
        else if (opening is { Waiting: true }) opening.Take();
        else opening = null;

        var start = opening;
        
        var result = probe is null
            ? editor.History.Patch.CompileForVideo(samples: samples, pictures: images, played: true)
            : editor.History.Patch.CompileForProbe(probe.Id, samples: samples, pictures: images, played: true);

        // Not a picture that is never drawn: a hidden preview would hold the cue until it gave up.
        if (start is not null && HasPicture) result.Program.WaitFor(start);

        var relaid = transport.Load(editor.History.Patch, result.Program, samples, start);

        reactions.Raise(new PatchCompiled());

        // What the ear reaches is said too. Compiling backwards from one sink
        // means the video pass never visits a module only the speakers reach —
        // and stops at the first line when there is no screen at all — so a
        // patch built for sound had nothing said about it, however wrong it was.
        var said = result.Issues
            .Concat(editor.History.Patch.CompileForAudio(samples: samples).Issues)
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

        transport.Pause();
        reactions.Raise(new TransportChanged());
    }

    /// <summary>Plays on from where it stopped, or from nought where it stopped at the end of its length.</summary>
    public void Resume()
    {
        if (!Paused) return;

        if (transport.Time >= Length) transport.Rewind();

        transport.Resume();
        SyncAudioToVolume();
    }

    /// <summary>Silences the speakers without stopping the device, so the clock does not drift.</summary>
    public void ToggleMute()
    {
        usage.Count(Used.Muted);
        transport.Mute(!Muted);
        reactions.Raise(new TransportChanged());
    }

    /// <summary>Puts a patch that has just been read on the canvas, from its beginning.</summary>
    public void Show(Patch patch)
    {
        reactions.Raise(new PatchShowing());

        editor.History.Open(patch);
        Rewind();
    }

    /// <summary>Takes the picture and the sound back to zero seconds.</summary>
    public void Rewind() => transport.Rewind();

    /// <summary>Rewinds because somebody pressed Rewind, which the run counts.</summary>
    public void RewindPressed()
    {
        usage.Count(Used.Rewound);
        Rewind();
    }

    /// <inheritdoc cref="Transport.SeekTo"/>
    public void SeekTo(double seconds) => transport.SeekTo(seconds);

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
        reactions.Raise(new TransportChanged());

        var wanted = !Paused && Audible;

        // Never off in the middle of a take. One with sound in it is paced by the
        // samples it is handed, so a device stopped under it stops the file —
        // picture and all — at that instant, and fading Volume to nought is how
        // a take is ended. It records the silence instead, and the device is
        // asked about again when the take is over.
        if (!wanted && recording.Running) return;

        // Nor while a preset from the gallery is being heard through it, which
        // is what started it if the patch had not.
        if (!wanted && audio.IsAuditioning) return;

        if (wanted != audio.IsRunning) SetAudioEnabled(wanted);
    }

    private void SetAudioEnabled(bool enabled)
    {
        if (!enabled)
        {
            transport.Stop();
            return;
        }

        // Not audio.Update: Recompile, the only caller that reaches here, has already
        // handed the engine a program built from the patch's sounds.
        try
        {
            transport.Start();
        }
        catch (Exception ex)
        {
            // A device is only really opened here, so this is where a card that is
            // busy, unplugged or missing its library says so. Blocked rather than
            // retried, and never taking the shell down (ADR-0025).
            report.Say($"Sound could not start — {ex.Message}", FailureDetail());
            blocked = true;
            return;
        }

        reactions.Raise(new PlaybackStarted());
    }
}
