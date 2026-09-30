using System.Diagnostics;
using Avalonia.Threading;
using Flyback.Core.Render;

namespace Flyback.App.Audio;

/// <summary>
/// Keeps the live sound at an oversampling the machine can play: twice a second it asks
/// <see cref="OversampleStepDown"/>, and lowers the factor when that says so.
/// </summary>
/// <param name="enabled">Whether lowering is allowed, read at every look.</param>
/// <param name="say">Where to say that it was lowered.</param>
internal sealed class LiveOversample(IAudioEngine audio, Func<bool> enabled, Action<string> say)
{
    private static readonly TimeSpan Every = TimeSpan.FromMilliseconds(500);

    private readonly OversampleStepDown judge = new();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private DispatcherTimer? timer;

    public void Start()
    {
        timer ??= new DispatcherTimer(Every, DispatcherPriority.Background, (_, _) => Look());
        timer.Start();
    }

    private void Look()
    {
        var from = audio.Oversample;

        // A take is written whole whatever the speakers do, so it keeps the factor it started at.
        var judged = audio.IsRunning && audio.Capture is null && enabled();

        if (judge.Check(clock.Elapsed, judged, audio.Timing, from).Lower is not { } lower) return;

        audio.Oversample = lower;
        say($"The sound kept falling behind at {OversamplingText.Of(from)}, so it is worked out with {OversamplingText.Of(lower)} now. Settings → Sound sets it back.");
    }
}
