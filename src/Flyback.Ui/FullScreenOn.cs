namespace Flyback.App;

/// <summary>Which monitor the full-screen picture goes to.</summary>
public enum FullScreenOn
{
    /// <summary>The one the window is on, which the picture takes over.</summary>
    SameMonitor,

    /// <summary>Any monitor but the window's, leaving the editor where it is.</summary>
    OtherMonitor,

    /// <summary>The monitor named in the settings, wherever the window is.</summary>
    ChosenMonitor,
}
