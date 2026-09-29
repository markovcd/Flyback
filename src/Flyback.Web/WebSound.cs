using System.Diagnostics;
using System.Runtime.Versioning;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;

namespace Flyback.Web;

/// <summary>
/// One patch's sound, playing in the viewer's worker: rendered a buffer at a time for
/// the speaker's queue, with what the picture needs to know of it packed on request.
/// </summary>
/// <remarks>
/// The sound runs as JavaScript where <see cref="JsSound"/> can make it, and on the
/// interpreter where it cannot. The picture's program is compiled here too, for the
/// Meters it reads, and is never drawn.
/// </remarks>
[SupportedOSPlatform("browser")]
internal sealed class WebSound : IDisposable
{
    private readonly CompiledPatch sound;
    private readonly CompiledPatch picture;
    private readonly AudioRenderer speakers;
    private readonly DelayState? memory;
    private readonly LiveValues heard;
    private readonly LiveValues shown;
    private readonly JsSound? script;
    private readonly Stopwatch rendering = new();
    private readonly VoicePool typed = new(MidiSources.Keyboard);
    private readonly LiveValues[] blocks;

    private double rendered;

    public WebSound(Opened opened, int width, int height)
    {
        var (patch, samples, pictures) = opened;

        Length = patch.Length;

        sound = patch.CompileForAudio(samples: samples, pictures: pictures, played: true).Program;
        picture = patch.CompileForVideo(samples: samples, pictures: pictures, played: true).Program;

        speakers = new AudioRenderer { Aspect = SynthRenderer.AspectOf(width, height) };
        speakers.Prepare(sound);
        memory = speakers.DelayMemoryFor(sound);

        heard = new LiveValues(sound.LiveInputs);
        shown = new LiveValues(picture.LiveInputs);
        blocks = [shown, heard];

        // Where the panel's knobs rest until somebody turns them.
        patch.Seed(heard);
        patch.Seed(shown);

        script = JsSound.Create(sound, memory, heard, speakers, out var why);
        Interpreted = why;
    }

    /// <summary>Why the sound runs on the interpreter rather than as JavaScript, or null when it does not.</summary>
    public string? Interpreted { get; }

    /// <summary>How long the patch plays for, in seconds, or null for one that has not said and plays on.</summary>
    public double? Length { get; }

    public int SoundOps => sound.Ops.Length;

    public int SampleRate => speakers.SampleRate;

    /// <summary>Where the sound has been rendered up to, in seconds.</summary>
    public double Time => speakers.Time;

    /// <summary>Seconds of sound rendered for each second spent rendering it, or zero before any.</summary>
    public double Speed => rendering.Elapsed.TotalSeconds > 0 ? rendered / rendering.Elapsed.TotalSeconds : 0;

    /// <summary>How many floats <see cref="Listen"/> packs.</summary>
    public int StateLength => SoundState.Length(picture);

    /// <summary>Fills <paramref name="interleavedStereo"/> with the next stretch of sound.</summary>
    public void Hear(Span<float> interleavedStereo)
    {
        rendering.Start();

        if (script is not null) script.Render(interleavedStereo);
        else speakers.Render(sound, interleavedStereo, memory, heard);

        rendering.Stop();

        rendered += interleavedStereo.Length / 2.0 / speakers.SampleRate;
    }

    /// <summary>
    /// Measures every Meter from what has been rendered, playing the readings into the
    /// sound too, and packs what the picture needs into <paramref name="state"/>.
    /// </summary>
    public void Listen(Span<float> state)
    {
        Meters.Refresh(sound, memory, shown, heard);

        SoundState.Write(picture, shown, state);
    }

    /// <summary>Turns a panel knob, 0 to 1.</summary>
    public void Turn(string key, float value)
    {
        heard.Set(key, value);
        shown.Set(key, value);
    }

    /// <summary>Whether either program reads the computer keyboard.</summary>
    public bool Played => blocks.Any(block => block.Keys.Any(key => MidiSignal.SourceOf(key) == MidiSources.Keyboard));

    /// <summary>A note on the computer keyboard pressed or let go, given to a voice as the editor gives it.</summary>
    public void Strike(int note, bool down)
    {
        if (down) typed.Down(note, ComputerKeyboard.Velocity, blocks);
        else typed.Up(note);

        Publish();
    }

    /// <summary>The computer keyboard notes sounding, one a voice.</summary>
    public IEnumerable<int> Sounding => typed.Voices.Where(voice => voice.Playing).Select(voice => (int)voice.Pitch);

    /// <summary>Every computer keyboard note let go.</summary>
    public void Release()
    {
        typed.Silence();
        Publish();
    }

    private void Publish()
    {
        foreach (var block in blocks) typed.WriteTo(block, blocks);
    }

    /// <summary>Where the knob called <paramref name="key"/> is turned to, or null where neither program reads it.</summary>
    public float? Reading(string key) => heard.Find(key) ?? shown.Find(key);

    /// <summary>
    /// How fast the sound renders here, measured on a copy so what is playing is
    /// untouched, and on the same backend.
    /// </summary>
    public double Measure(double seconds)
    {
        var copy = new AudioRenderer { Aspect = speakers.Aspect };
        var lines = copy.DelayMemoryFor(sound);
        var live = new LiveValues(sound.LiveInputs);
        using var timed = script is null ? null : JsSound.Create(sound, lines, live, copy, out _);
        var buffer = new float[1024];
        var buffers = Math.Max(1, (int)(seconds * copy.SampleRate / 512));

        var clock = Stopwatch.StartNew();

        for (var i = 0; i < buffers; i++)
        {
            if (timed is not null) timed.Render(buffer);
            else copy.Render(sound, buffer, lines, live);
        }

        return buffers * 512.0 / copy.SampleRate / clock.Elapsed.TotalSeconds;
    }

    /// <summary>Back to <paramref name="seconds"/>, with everything the patch remembers emptied.</summary>
    public void SeekTo(double seconds)
    {
        speakers.Reset();
        memory?.Clear();
        speakers.SeekTo(Math.Max(0, seconds));
    }

    /// <summary>Lets the script and the memory it pinned go.</summary>
    public void Dispose() => script?.Dispose();
}
