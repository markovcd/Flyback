using System.Diagnostics;
using Avalonia.Threading;
using Avalonia.Input;
using Flyback.App.Audio;
using Flyback.App.Controls;
using Flyback.App.Midi;
using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Flyback.Plugins.Audio;
using Flyback.Plugins.Midi;

namespace Flyback.Viewer;

/// <summary>
/// One patch, playing: its picture on a preview, its sound on a device, and the
/// transport between them. Nothing here is a window, so a run with none is the same
/// object without the preview.
/// </summary>
/// <remarks>
/// Sound leads and the picture follows, as in the editor: while the device runs, the
/// preview is timed by the sound's own clock. Stopped, the preview is held on a clock
/// of its own that does not move. The patch is compiled once and never again, so
/// nothing here reacts to an edit — there is nowhere to make one.
/// <para>
/// A patch that is played is played here too: the computer's keys and whatever MIDI
/// device its MIDI In names reach it through the editor's own <see cref="MidiHub"/>,
/// and a knob bound to a controller follows it through <see cref="ControlHub"/>. A
/// hand turns one through <see cref="Turn"/>.
/// </para>
/// </remarks>
internal sealed class ViewerPlayer : IDisposable
{
    private readonly ViewerOptions options;
    private readonly PreviewHost? preview;
    private readonly AudioEngine audio;
    private readonly IlCompiler compiler;
    private readonly MidiHub midi;
    private readonly ControlHub controls;
    private readonly WallClock clock;
    private readonly bool keyed;
    private readonly Patch patch;
    private bool audible;

    private DispatcherTimer? ticker;
    private TimeSpan last;
    private double played;
    private double sinceLoop;
    private bool finished;
    private double frozenAt;

    /// <param name="preview">The picture's surface, or null where there is no picture to draw.</param>
    /// <param name="audio">The sound engine, on the run's device or a silent one where it has none, compiling with <paramref name="compiler"/>.</param>
    public ViewerPlayer(
        ViewerLaunch launch,
        PreviewHost? preview,
        AudioEngine audio,
        IlCompiler compiler,
        MidiHub midi,
        ControlHub controls,
        WallClock clock)
    {
        var (opened, device, options, _, takeover) = launch;

        this.options = options;
        this.preview = options.Video ? preview : null;
        this.audio = audio;
        this.compiler = compiler;
        this.midi = midi;
        this.controls = controls;

        this.clock = clock;

        var (patch, samples, pictures) = opened;

        this.patch = patch;

        audio.Aspect = SynthRenderer.AspectOf(options.Size.Width, options.Size.Height);

        // Sound and picture start together, once both are built.
        var start = new Cue();
        audio.Update(patch, samples, start);

        // The panel's knobs are worth nothing until somebody writes them into the
        // block the programs read, which the editor does as it lays the panel out.
        // A played preset opens with its filter shut without this, and a patch whose
        // Volume follows a knob is heard as silence.
        patch.Seed(audio.Live);

        if (this.preview is { } surface)
        {
            surface.Resolution = options.Size;
            surface.Use(options.Gpu ? PreviewBackend.Gpu : PreviewBackend.Cpu);
            surface.FrameRate = options.FrameRate;

            var program = patch.CompileForVideo(samples: samples, pictures: pictures, played: true).Program;
            program.WaitFor(start);

            surface.Program = program;
            surface.Live = new LiveValues(program.LiveInputs);
            patch.Seed(surface.Live);

            // The renderer is the processor's for good if the graphics card refuses.
            Submit();
            surface.BackendChanged += _ => Submit();
        }

        start.Give();

        controls.Takeover = takeover;
        controls.Turned += (id, value) => Turned?.Invoke(id, value);

        midi.Trouble += message => Console.Error.WriteLine($"{GlobalConstants.ApplicationName}: {message}");

        // A key going down while the clock is stopped is a change with no time behind it.
        if (this.preview is { } redrawn) midi.Played += redrawn.Refresh;

        LiveValues[] blocks = this.preview is { } shown ? [shown.Live, audio.Live] : [audio.Live];

        midi.Lay(patch.KeyboardScale);
        midi.Follow(blocks);
        controls.Follow(patch, blocks);

        keyed = blocks.Any(block => block.Keys.Any(key => key.StartsWith(MidiSources.Keyboard + "/", StringComparison.Ordinal)));

        audible = device is not null && !options.NoAudio && Sound.VolumeIsUp(patch);
        Muted = options.Mute;
        Paused = options.Paused;

        audio.Gain = Muted ? 0f : options.Volume;

        if (options.From > 0)
        {
            audio.SeekTo(options.From);
            if (this.preview is not null) this.preview.Time = options.From;
        }

        frozenAt = options.From;
    }

    /// <summary>The sound engine, for the tests that read its clock and its blocks.</summary>
    internal AudioEngine Audio => audio;

    /// <summary>Completes once the patch opened here runs compiled, which is when it starts to play.</summary>
    internal Task Compiled() => compiler.Settled();

    /// <summary>Whether a sound device is running, or will be once play resumes.</summary>
    public bool Sounding => audible;

    public bool Paused { get; private set; }

    public bool Muted { get; private set; }

    /// <summary>Whether either running program reads the computer's keys, so a letter is a note.</summary>
    public bool Keyed => keyed;

    /// <summary>A key as a note, or as one of the pair that moves the rows. False where it is neither.</summary>
    public bool KeyDown(Key key) => keyed && (midi.Shift(key) is not null || midi.KeyDown(key));

    /// <summary>Lets a note go. Unguarded, since a missed release is a note that never ends.</summary>
    public void KeyUp(Key key) => midi.KeyUp(key);

    /// <summary>Lets every held note go, for a window that has lost the keyboard.</summary>
    public void AllOff() => midi.AllOff();

    /// <summary>Where the picture is, in seconds.</summary>
    public double Time => preview?.Time ?? audio.Time;

    /// <summary>The patch playing, whose knobs are there to be turned.</summary>
    public Patch Patch => patch;

    /// <summary>A controller turned a knob: its id and where it now sits. Raised on the driver's thread.</summary>
    public event Action<Guid, float>? Turned;

    /// <summary>Turns a knob by hand.</summary>
    public void Turn(Guid id, float value)
    {
        if (patch.Control(id) is not { } control) return;

        control.Value = value;
        controls.Set(id, value);
        preview?.Refresh();
    }

    /// <summary>Raised when <c>--for</c> has run out.</summary>
    public event Action? Finished;


    /// <summary>Starts playing, or holds the first frame where the run was asked to open paused.</summary>
    public void Begin()
    {
        last = clock.Elapsed;

        if (options.For is not null || options.Loop is not null)
        {
            ticker = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            ticker.Tick += (_, _) => Tick();
            ticker.Start();
        }

        Apply();
    }

    /// <summary>
    /// Adds the time played since the last tick, then finishes a <c>--for</c> that
    /// has run out or rewinds a <c>--loop</c> that has come round. Time paused is not counted.
    /// </summary>
    internal void Tick()
    {
        var now = clock.Elapsed;
        var delta = (now - last).TotalSeconds;

        last = now;

        if (Paused || finished) return;

        played += delta;
        sinceLoop += delta;

        if (options.For is { } seconds && played >= seconds)
        {
            finished = true;
            ticker?.Stop();
            Finished?.Invoke();

            return;
        }

        if (options.Loop is { } every && sinceLoop >= every) Rewind();
    }

    public void Pause()
    {
        if (Paused) return;

        Tick();

        frozenAt = preview?.Time ?? audio.Time;
        Paused = true;
        Apply();
    }

    public void Resume()
    {
        if (!Paused) return;

        last = clock.Elapsed;
        Paused = false;
        Apply();
    }

    public void Toggle()
    {
        if (Paused) Resume();
        else Pause();
    }

    public void Mute(bool muted)
    {
        Muted = muted;
        audio.Gain = muted ? 0f : options.Volume;
    }

    /// <summary>Back to nought. A run that is paused stays paused, on the first frame.</summary>
    public void Rewind()
    {
        audio.Rewind();
        preview?.Rewind();

        // Or the next tick reads the old frozen time and the rewind is undone.
        frozenAt = 0;
        sinceLoop = 0;

        if (preview is not null) preview.Time = 0;
    }

    /// <summary>Puts the sound and the picture's clock into whichever state <see cref="Paused"/> says.</summary>
    private void Apply()
    {
        if (Paused)
        {
            // Both surfaces skip a frame whose clock has not moved, and take the
            // time of the last tick before they look, so resuming adds one frame
            // rather than the whole pause. Held even where nothing is playing.
            if (preview is not null) preview.Clock = () => frozenAt;

            audio.Stop();

            if (preview is not null) audio.Deafen(preview.Live);

            return;
        }

        if (audible && Start())
        {
            if (preview is { } surface)
            {
                surface.Clock = () =>
                {
                    audio.Listen(surface.Program, surface.Live);
                    return audio.Time;
                };
            }

            return;
        }

        if (preview is not null) preview.Clock = null;
    }

    private bool Start()
    {
        try
        {
            audio.Start();

            return true;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"sound: {ex.Message}");
            Console.Error.WriteLine($"{GlobalConstants.ApplicationName}: no sound — {ex.Message}");

            // Not tried again on every resume, only to say the same thing again.
            audible = false;

            return false;
        }
    }

    private void Submit()
    {
        if (preview is { Backend: PreviewBackend.Cpu } surface) compiler.Submit(surface.Program, IlLane.Picture);
    }

    public void Dispose()
    {
        ticker?.Stop();

        midi.Dispose();

        audio.Stop();
        audio.Dispose();
        compiler.Dispose();
    }
}
