namespace Flyback.App.Capture;

/// <summary>The recording state shared with playback without coupling their lifetimes.</summary>
internal sealed class RecordingState
{
    public bool Running { get; private set; }

    /// <summary>Whether a take is running, or stopped and its file not yet closed.</summary>
    public bool InHand { get; private set; }

    internal void SetRunning(bool running) => Running = running;

    internal void SetInHand(bool inHand) => InHand = inHand;
}
