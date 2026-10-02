namespace Flyback.Editor.Capture;

/// <summary>How a recording is going, for the status bar to read.</summary>
internal readonly record struct RecordingStatus(
    double Seconds,
    long Frames,
    long Duplicated,
    long AudioDropped,
    string? Stopped);