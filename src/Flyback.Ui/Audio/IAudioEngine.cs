using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Render;
using Flyback.Plugins.Audio;
using Flyback.Ui.Capture;

namespace Flyback.Ui.Audio;

/// <summary>
/// The sound of a patch being edited: its program, the device it plays through, and
/// the clock the picture follows while it plays. <see cref="AudioEngine"/> on a desktop;
/// a page plays it in a worker of its own (ADR-0162).
/// </summary>
internal interface IAudioEngine
{
    bool IsRunning { get; }

    /// <summary>How many ops the sound's program runs for each sample.</summary>
    int Ops { get; }

    /// <summary>The rate the sound plays at, which a recording has to match.</summary>
    int SampleRate { get; }

    /// <summary>Where a recording listens, while one is running.</summary>
    IAudioSink? Capture { get; set; }

    /// <summary>Where the sound has got to, in seconds: the clock the picture follows while it plays.</summary>
    double Time { get; }

    /// <summary>The shape Coordinates' <c>aspect</c> reads while playing live, following the preview's resolution.</summary>
    float Aspect { get; set; }

    /// <summary>How loud the speakers are turned down to, from 0 to 1.</summary>
    float Gain { get; set; }

    /// <summary>How many times the output rate the sound is evaluated at. Changing it starts what the patch remembers anew.</summary>
    int Oversample { get; set; }

    /// <summary>How many buffers of compiled sound have been timed, and how many of them took longer to make than they play for.</summary>
    SoundTiming Timing { get; }

    /// <summary>How many times real time the sound renders at lately, or 0 while nothing is measured.</summary>
    double Speed { get; }

    /// <summary>The block whoever is playing writes into, made anew by every <see cref="Update"/>.</summary>
    LiveValues Live { get; }

    /// <summary>Whether a preset has been asked to be heard over the patch.</summary>
    bool IsAuditioning { get; }

    void Start();

    void Stop();

    /// <summary>Plays through <paramref name="next"/> from here on, and says whether it was taken.</summary>
    bool Use(IAudioDevice next);

    /// <summary>Takes the sound, and what its program remembers, back to nought.</summary>
    void Rewind();

    /// <summary>Plays on from <paramref name="seconds"/>, with what the program remembers emptied.</summary>
    void SeekTo(double seconds);

    /// <summary>Swaps in <paramref name="patch"/>'s sound, silent until <paramref name="start"/> goes.</summary>
    /// <param name="sound">The patch's sound compiled with <c>played: true</c> already, or null to compile it here.</param>
    void Update(Patch patch, ISampleLibrary? samples = null, Cue? start = null, CompiledPatch? sound = null);

    /// <summary>Hands the picture what has been played: every Scope in <paramref name="drawn"/> and every Meter in <paramref name="watching"/>.</summary>
    void Listen(CompiledPatch drawn, LiveValues watching);

    /// <summary>Every Meter in <paramref name="watching"/> back to nothing, for a picture drawn without the sound.</summary>
    void Deafen(LiveValues watching);

    /// <summary>A preset made ready to be heard over the patch, or null for one that makes no sound.</summary>
    AudioEngine.Audition? PrepareAudition(Patch patch, ISampleLibrary? samples = null);

    void StartAudition(AudioEngine.Audition audition);

    void EndAudition();
}
