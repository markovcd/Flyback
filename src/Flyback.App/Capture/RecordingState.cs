namespace Flyback.App.Capture;

/// <summary>The recording state shared with playback without coupling their lifetimes.</summary>
internal sealed class RecordingState
{
    public bool Running { get; private set; }

    internal void SetRunning(bool running) => Running = running;
}
