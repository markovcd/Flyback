using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Engine.Render;
using Flyback.Plugins.Audio;
using Flyback.Ui.Capture;

namespace Flyback.Ui.Audio;

/// <summary>
/// A sound this process never plays: compiled only for its live inputs and its size, through
/// no device, with no preset auditioned and nothing recorded.
/// </summary>
internal abstract class UnplayedSound : IAudioEngine
{
    public bool IsRunning { get; private set; }

    public int Ops { get; private set; }

    public int SampleRate => GlobalConstants.SampleRate;

    public virtual string? Backend => null;

    public IAudioSink? Capture { get; set; }

    public abstract double Time { get; }

    public abstract float Aspect { get; set; }

    public abstract float Gain { get; set; }

    public abstract int Oversample { get; set; }

    public SoundTiming Timing => default;

    public abstract double Speed { get; }

    public LiveValues Live { get; private set; } = LiveValues.None;

    public bool IsAuditioning => false;

    public virtual void Start() => IsRunning = true;

    public virtual void Stop() => IsRunning = false;

    public bool Use(AudioSetup next) => false;

    public void Rewind() => SeekTo(0);

    public abstract void SeekTo(double seconds);

    public void Update(Patch patch, ISampleLibrary? samples = null, Cue? start = null, CompiledPatch? sound = null, Action<LiveValues>? seed = null)
    {
        var program = sound ?? patch.CompileForAudio(samples: samples, played: true).Program;

        Ops = program.Ops.Length;
        Live = new LiveValues(program.LiveInputs);
        seed?.Invoke(Live);
        Updated(patch, samples);
    }

    public abstract void Listen(CompiledPatch drawn, LiveValues watching);

    public void Deafen(LiveValues watching)
    {
        foreach (var key in watching.Keys)
            if (MeterSignals.Is(key)) watching.Set(key, 0f);
    }

    public AudioEngine.Audition? PrepareAudition(Patch patch, ISampleLibrary? samples = null) => null;

    public void StartAudition(AudioEngine.Audition audition) { }

    public void EndAudition() { }

    /// <summary>Runs after <see cref="Update"/> has made <see cref="Live"/> anew and seeded it.</summary>
    protected virtual void Updated(Patch patch, ISampleLibrary? samples) { }
}
