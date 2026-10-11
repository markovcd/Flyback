namespace Flyback.Editor.Notices;

/// <summary>The sound device could not be opened or started, with the line that says so; what loaded this run is its detail.</summary>
internal sealed record SoundFailed(string Message) : INotice;
