namespace Flyback.Editor;

/// <summary>What pressing Measure would do now, which is what its button shows.</summary>
internal enum MeasureMode
{
    /// <summary>Measure: nothing is pinned, or what is pinned is out of date.</summary>
    Ready,

    /// <summary>Stop the measurement under way.</summary>
    Running,

    /// <summary>Hide the labels, which are up to date.</summary>
    Shown,
}
