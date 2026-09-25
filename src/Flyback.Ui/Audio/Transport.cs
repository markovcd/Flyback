using Flyback.App.Controls;
using Flyback.App.Midi;
using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.App.Audio;

/// <summary>
/// Play, pause, mute and rewind for a picture and a sound played together, in the
/// editor and the viewer alike.
/// </summary>
/// <remarks>
/// Sound cannot stretch, so it leads: while the device runs, the picture is timed by
/// its clock. Paused, the picture holds still on a clock of its own and a resume adds
/// one frame rather than the whole pause. Whether and when to start the device is the
/// shell's to decide.
/// </remarks>
internal sealed class Transport
{
    private readonly AudioEngine audio;
    private readonly PreviewHost? preview;
    private readonly IlCompiler compiler;
    private readonly MidiHub midi;

    /// <summary>Where the picture is held while <see cref="Paused"/>.</summary>
    private double frozenAt;

    /// <param name="preview">The picture's surface, or null where there is no picture.</param>
    public Transport(AudioEngine audio, PreviewHost? preview, IlCompiler compiler, MidiHub midi)
    {
        this.audio = audio;
        this.preview = preview;
        this.compiler = compiler;
        this.midi = midi;

        // The renderer is the processor's for good if the graphics card refuses.
        if (preview is not null) preview.BackendChanged += _ => Submit();
    }

    public bool Paused { get; private set; }

    public bool Muted { get; private set; }

    /// <summary>How loud the sound is while not muted.</summary>
    public float Volume { get; init; } = 1f;

    /// <summary>Where the picture is, in seconds, or the sound where there is no picture.</summary>
    public double Time => preview?.Time ?? audio.Time;

    /// <summary>The blocks the running programs read: the picture's, where there is one, and the sound's.</summary>
    public LiveValues[] Blocks => preview is { } surface ? [surface.Live, audio.Live] : [audio.Live];

    /// <summary>Whether either running program reads the computer's keys, so a letter is a note.</summary>
    /// <remarks>Asked of the compiled programs, where dead-code elimination has already left out a MIDI In wired to nothing (ADR-0022).</remarks>
    public bool Keyed => Blocks.Any(block => block.Keys.Any(key => key.StartsWith(MidiSources.Keyboard + "/", StringComparison.Ordinal)));

    /// <summary>
    /// Hands the picture <paramref name="picture"/> and the sound a program of
    /// <paramref name="patch"/>, both starting on <paramref name="start"/>, and keeps
    /// whatever is held playing in the new blocks.
    /// </summary>
    /// <returns>Whether the keyboard was laid out anew for the patch's scale.</returns>
    public bool Load(Patch patch, CompiledPatch? picture, ISampleLibrary? samples, Cue? start)
    {
        if (preview is not null && picture is not null)
        {
            preview.Program = picture;
            Submit();
        }

        audio.Update(patch, samples, start);
        start?.Give();

        // A note held through an edit is written into the new blocks before the next
        // frame or buffer, so it is not cut off.
        if (preview is not null && picture is not null) preview.Live = new LiveValues(picture.LiveInputs);

        var relaid = midi.Lay(patch.Keyboard);
        midi.Follow(Blocks);

        return relaid;
    }

    /// <summary>Starts the device, with the picture following its clock. Throws whatever the device throws.</summary>
    public void Start()
    {
        audio.Start();

        // Each tick also hands the picture what the speakers just played: a Scope's
        // chart and a Meter's reading. No sound, no chart.
        if (preview is { } surface)
        {
            surface.Clock = () =>
            {
                audio.Listen(surface.Program, surface.Live);
                return audio.Time;
            };
        }
    }

    /// <summary>
    /// Stops the device. The picture holds still where paused and runs on its own
    /// where not, and every Meter reads nought rather than its last reading.
    /// </summary>
    public void Stop()
    {
        if (preview is not null) preview.Clock = Paused ? () => frozenAt : null;

        audio.Stop();

        if (preview is not null) audio.Deafen(preview.Live);
    }

    /// <summary>Stops the device and holds the picture where it is.</summary>
    public void Pause()
    {
        if (Paused) return;

        frozenAt = Time;
        Paused = true;

        Stop();
    }

    /// <summary>Lets the picture run on its own clock. The shell starts the device again if it wants sound.</summary>
    public void Resume()
    {
        if (!Paused) return;

        Paused = false;

        if (preview is not null) preview.Clock = null;
    }

    /// <summary>Silences the speakers without stopping the device, so the clock does not drift.</summary>
    public void Mute(bool muted)
    {
        Muted = muted;
        audio.Gain = muted ? 0f : Volume;
    }

    /// <summary>Takes the picture and the sound back to nought. A paused run stays paused, on the first frame.</summary>
    public void Rewind()
    {
        audio.Rewind();
        preview?.Rewind();

        // A paused clock reads the time it froze at, so the next tick would undo the rewind.
        if (!Paused) return;

        frozenAt = 0;
        if (preview is not null) preview.Time = 0;
    }

    /// <summary>
    /// Takes the picture and the sound to <paramref name="seconds"/>, playing on from
    /// there, or held there while paused. What the patch remembers starts empty.
    /// </summary>
    public void SeekTo(double seconds)
    {
        seconds = double.IsFinite(seconds) ? Math.Max(0, seconds) : 0;

        audio.SeekTo(seconds);

        if (preview is not null)
        {
            preview.Rewind();
            preview.Time = seconds;
        }

        if (Paused) frozenAt = seconds;
    }

    private void Submit()
    {
        if (preview is { Backend: PreviewBackend.Cpu } surface) compiler.Submit(surface.Program, IlLane.Picture);
    }
}
