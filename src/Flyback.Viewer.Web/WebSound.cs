using System.Diagnostics;
using System.Runtime.Versioning;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Engine.Graph;
using Flyback.Engine.Render;

namespace Flyback.Viewer.Web;

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
    private readonly CompiledPatch? picture;
    private AudioRenderer speakers;
    private DelayState? memory;
    private readonly LiveValues heard;
    private readonly LiveValues shown;
    private JsSound? script;
    private readonly Stopwatch rendering = new();
    private readonly VoicePool typed = new(MidiSources.Keyboard);
    private readonly LiveValues[] blocks;
    private readonly SoundPace pace;
    private readonly LineInFeed microphone;

    private double rendered;

    /// <summary>
    /// Frames of the microphone held back before it is played, so the chunks the worker
    /// renders ahead of the speaker are not caught short by when its frames arrive.
    /// </summary>
    private const int MicrophoneCushion = 2048;

    public WebSound(Opened opened, int width, int height)
        : this(opened, SynthRenderer.AspectOf(width, height), withPicture: true, after: null)
    {
    }

    /// <summary>
    /// The sound of an edited patch, playing on from <paramref name="after"/> with its
    /// clock, what it remembers and how it has kept pace, as the editor's engine carries
    /// them through an edit.
    /// </summary>
    public WebSound(Opened opened, float aspect, WebSound? after)
        : this(opened, aspect, withPicture: false, after)
    {
    }

    /// <param name="withPicture">
    /// Whether the picture's program is compiled too, for the viewer's Meters. The
    /// editor says which Meters it wants by name instead, through <see cref="Readings"/>.
    /// </param>
    private WebSound(Opened opened, float aspect, bool withPicture, WebSound? after)
    {
        var (patch, samples, pictures) = opened;

        Length = patch.Length;

        sound = patch.CompileForAudio(samples: samples, pictures: pictures, played: true).Program;
        picture = withPicture ? patch.CompileForVideo(samples: samples, pictures: pictures, played: true).Program : null;

        microphone = after?.microphone ?? new LineInFeed { Cushion = MicrophoneCushion };
        speakers = after?.speakers ?? new AudioRenderer();
        speakers.Input = microphone;
        speakers.Aspect = aspect;
        speakers.Prepare(sound);
        memory = speakers.DelayMemoryFor(sound, after?.memory);
        pace = after?.pace ?? new SoundPace();

        heard = Emptied(after?.heard, sound.LiveInputs) ?? new LiveValues(sound.LiveInputs);
        shown = picture is null ? LiveValues.None : new LiveValues(picture.LiveInputs);
        blocks = picture is null ? [heard] : [shown, heard];

        // Where the panel's knobs rest until somebody turns them.
        patch.Seed(heard);
        patch.Seed(shown);

        if (after?.script is { } playing && playing.Retune(sound, memory, heard, speakers))
        {
            script = playing;
            after.script = null;
        }
        else
        {
            script = JsSound.Create(sound, memory, heard, speakers, out var why);
            Interpreted = why;
        }
    }

    /// <summary>
    /// <paramref name="kept"/> with every value back at nought, as a new block starts, where it
    /// names the same live inputs; kept so a script retuned onto the edit reads where it did.
    /// </summary>
    private static LiveValues? Emptied(LiveValues? kept, IReadOnlyList<string> keys)
    {
        if (kept is null || !kept.Keys.SequenceEqual(keys)) return null;

        Array.Clear(kept.Storage);
        return kept;
    }

    /// <summary>Why the sound runs on the interpreter rather than as JavaScript, or null when it does not.</summary>
    public string? Interpreted { get; private set; }

    /// <summary>
    /// How many times the output rate the sound is worked out at. Changing it plays on from
    /// where the sound was, at the new rate, with what the patch remembers emptied.
    /// </summary>
    public int Oversample
    {
        get => speakers.Oversample;
        set
        {
            if (value != speakers.Oversample) WorkOutAt(value);
        }
    }

    /// <inheritdoc cref="SoundPace.Timing"/>
    public SoundTiming Timing => pace.Timing;

    /// <inheritdoc cref="SoundPace.Behind"/>
    public bool Behind => pace.Behind;

    /// <summary>
    /// Judges the chunks heard since the last look, and works the sound out a step lower
    /// when they keep falling behind.
    /// </summary>
    /// <inheritdoc cref="OversampleStepDown.Check" path="/param"/>
    public void Judge(TimeSpan now, bool playing)
    {
        if (pace.Judge(now, playing, Oversample) is { } lower) WorkOutAt(lower);
    }

    /// <summary>Plays on at <paramref name="factor"/>, on a new script that is left to settle before it is judged.</summary>
    private void WorkOutAt(int factor)
    {
        var at = speakers.Time;

        script?.Dispose();

        speakers = new AudioRenderer(speakers.SampleRate, factor) { Aspect = speakers.Aspect, Input = microphone };
        speakers.Prepare(sound);
        speakers.SeekTo(at);
        memory = speakers.DelayMemoryFor(sound);

        script = JsSound.Create(sound, memory, heard, speakers, out var why);
        Interpreted = why;
        pace.Renew();
    }

    /// <summary>Whether the sound reads a Line In, so the page should have the microphone open.</summary>
    public bool HearsMicrophone => sound.LiveInputs.Contains(LineInSignal.Left) || sound.LiveInputs.Contains(LineInSignal.Right);

    /// <summary>Takes what the microphone just heard, as interleaved stereo frames.</summary>
    public void Overhear(ReadOnlySpan<float> interleavedStereo) => microphone.Write(interleavedStereo, channels: 2);

    /// <summary>How long the patch plays for, in seconds, or null for one that has not said and plays on.</summary>
    public double? Length { get; }

    public int SoundOps => sound.Ops.Length;

    public int SampleRate => speakers.SampleRate;

    /// <inheritdoc cref="AudioRenderer.Aspect"/>
    public float Aspect
    {
        get => speakers.Aspect;
        set => speakers.Aspect = value;
    }

    /// <summary>Where the sound has been rendered up to, in seconds.</summary>
    public double Time => speakers.Time;

    /// <summary>Seconds of sound rendered for each second spent rendering it, or zero before any.</summary>
    public double Speed => rendering.Elapsed.TotalSeconds > 0 ? rendered / rendering.Elapsed.TotalSeconds : 0;

    /// <summary>How many floats <see cref="Listen"/> packs.</summary>
    public int StateLength => picture is null ? 0 : SoundState.Length(picture);

    /// <summary>Fills <paramref name="interleavedStereo"/> with the next stretch of sound.</summary>
    /// <param name="judged">Whether the chunk is heard, and so counts to how the sound keeps pace; a warm-up's is not.</param>
    public void Hear(Span<float> interleavedStereo, bool judged)
    {
        var started = rendering.Elapsed;
        rendering.Start();

        if (script is not null) script.Render(interleavedStereo);
        else speakers.Render(sound, interleavedStereo, memory, heard);

        rendering.Stop();

        var plays = interleavedStereo.Length / 2.0 / speakers.SampleRate;
        rendered += plays;

        if (judged) pace.Count(rendering.Elapsed - started, TimeSpan.FromSeconds(plays));
    }

    /// <summary>
    /// Measures every Meter and refills every chart from what has been rendered, playing
    /// the readings into the sound too, and packs what the picture needs into <paramref name="state"/>.
    /// </summary>
    public void Listen(Span<float> state)
    {
        if (picture is null) return;

        Meters.Refresh(sound, memory, shown, heard);
        Traces.Refresh(picture, sound, memory);

        SoundState.Write(picture, shown, state);
    }

    /// <summary>
    /// Measures the Meters <paramref name="watched"/> is keyed by, playing the readings
    /// into the sound too, and leaves them in its storage in the order of its keys; and
    /// refills <paramref name="charts"/>.
    /// </summary>
    public void Readings(LiveValues watched, IReadOnlyList<TapSpec> charts)
    {
        Meters.Refresh(sound, memory, watched, heard);
        Traces.Refill(charts, sound, memory);
    }

    /// <summary>Plays <paramref name="value"/> on <paramref name="key"/>, as it was written into the editor's block.</summary>
    public void Play(string key, float value) => heard.Set(key, value);

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
