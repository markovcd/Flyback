namespace Flyback.Ui.Controls;

/// <summary>What a key does over a picture that has the screen.</summary>
public enum StageKey
{
    /// <summary>Not one of the stage's keys.</summary>
    None,

    /// <summary>Escape: gives the screen back.</summary>
    Leave,

    /// <summary>F11: takes the screen, or gives it back.</summary>
    FullScreen,

    /// <summary>F3: shows or hides the line saying how the picture is drawn.</summary>
    Stats,

    /// <summary>Space or Ctrl+P: plays or pauses.</summary>
    Pause,
}
