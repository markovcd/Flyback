using Flyback.Engine.Compile;
using Flyback.Ui.Audio;
using Flyback.Ui.Capture;
using Flyback.Core;
using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Render;
using Flyback.Plugins.Audio;

namespace Flyback.Editor.Desktop.Shots;

/// <summary>
/// The sound of a shot: compiled, so the window says what it costs, and never played. Its
/// clock is wherever the shot puts it, and the picture follows it as it follows a speaker.
/// </summary>
/// <remarks>Nothing is heard, so a Scope or an Analyzer has nothing to chart.</remarks>
internal sealed class ShotSound : IAudioEngine
{
    public bool IsRunning { get; private set; }

    public int Ops { get; private set; }

    public int SampleRate => GlobalConstants.SampleRate;

    public IAudioSink? Capture { get; set; }

    public double Time { get; set; }

    public float Aspect { get; set; } = 1f;

    public int Oversample { get; set; } = AudioRenderer.DefaultOversample;

    public SoundTiming Timing => default;

    public double Speed => 0;

    public float Gain { get; set; } = 1f;

    public LiveValues Live { get; private set; } = LiveValues.None;

    public bool IsAuditioning => false;

    public void Start() => IsRunning = true;

    public void Stop() => IsRunning = false;

    public bool Use(IAudioDevice next) => false;

    public void Rewind() => Time = 0;

    public void SeekTo(double seconds) => Time = double.IsFinite(seconds) ? Math.Max(0, seconds) : 0;

    public void Update(Patch patch, ISampleLibrary? samples = null, Cue? start = null, CompiledPatch? sound = null, Action<LiveValues>? seed = null)
    {
        var program = sound ?? patch.CompileForAudio(samples: samples, played: true).Program;

        Ops = program.Ops.Length;
        Live = new LiveValues(program.LiveInputs);
        seed?.Invoke(Live);
    }

    public void Listen(CompiledPatch drawn, LiveValues watching)
    {
    }

    public void Deafen(LiveValues watching)
    {
        foreach (var key in watching.Keys)
            if (MeterSignals.Is(key)) watching.Set(key, 0f);
    }

    public AudioEngine.Audition? PrepareAudition(Patch patch, ISampleLibrary? samples = null) => null;

    public void StartAudition(AudioEngine.Audition audition)
    {
    }

    public void EndAudition()
    {
    }
}
