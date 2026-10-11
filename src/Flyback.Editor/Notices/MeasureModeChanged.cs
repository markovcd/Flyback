namespace Flyback.Editor.Notices;

/// <summary>What pressing Measure would do now has changed, which is what its button shows.</summary>
internal sealed record MeasureModeChanged(MeasureMode Mode) : INotice;
