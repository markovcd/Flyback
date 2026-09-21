using System.Diagnostics;
using Avalonia.Threading;
using Flyback.App.Audio;
using Flyback.App.Controls;
using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Flyback.Plugins.Audio;

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
/// </remarks>
internal sealed class ViewerPlayer : IDisposable
{
    private readonly ViewerOptions options;
    private readonly PreviewHost? preview;
    private readonly AudioEngine audio;
    private readonly IlCompiler compiler = new();
    private bool audible;

    private DispatcherTimer? ticker;
    private TimeSpan last;
    private double played;
    private double sinceLoop;
    private bool finished;
    private double frozenAt;

    /// <param name="preview">The picture's surface, or null where there is no picture to draw.</param>
    /// <param name="device">The sound device, or null where there is none to play through.</param>
    public ViewerPlayer(Opened opened, IAudioDevice? device, ViewerOptions options, PreviewHost? preview)
    {
        this.options = options;
        this.preview = options.Video ? preview : null;

        compiler.Enabled = !options.Interpreted;
        audio = new AudioEngine(device ?? new SilentAudioDevice()) { Compiler = compiler };

        var (patch, samples, pictures) = opened;

        audio.Aspect = SynthRenderer.AspectOf(options.Size.Width, options.Size.Height);
        audio.Update(patch, samples);

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

            surface.Program = program;
            surface.Live = new LiveValues(program.LiveInputs);
            patch.Seed(surface.Live);

            // The renderer is the processor's for good if the graphics card refuses.
            Submit();
            surface.BackendChanged += _ => Submit();
        }

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

    /// <summary>Whether a sound device is running, or will be once play resumes.</summary>
    public bool Sounding => audible;

    public bool Paused { get; private set; }

    public bool Muted { get; private set; }

    /// <summary>Where the picture is, in seconds.</summary>
    public double Time => preview?.Time ?? audio.Time;

    /// <summary>Raised when <c>--for</c> has run out.</summary>
    public event Action? Finished;

    /// <summary>The wall clock <c>--for</c> and <c>--loop</c> count played time against.</summary>
    internal Func<TimeSpan> Now { get; init; } = Watch();

    /// <summary>Starts playing, or holds the first frame where the run was asked to open paused.</summary>
    public void Begin()
    {
        last = Now();

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
        var now = Now();
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

        last = Now();
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

    private static Func<TimeSpan> Watch()
    {
        var watch = Stopwatch.StartNew();

        return () => watch.Elapsed;
    }

    public void Dispose()
    {
        ticker?.Stop();

        audio.Stop();
        audio.Dispose();
        compiler.Dispose();
    }
}
