namespace Flyback.Editor.Notices;

/// <summary>A moment in the editor's start that a part may have something to do at.</summary>
internal enum StartPhase
{
    /// <summary>The container has built the editor; nothing holds it yet. What was last saved is put in force here.</summary>
    Built,

    /// <summary>A window or a page holds it and is about to show it: the saved layout and the first patch.</summary>
    Shown,

    /// <summary>The window is open, so a dialog can show and a file can be resolved.</summary>
    Opened,
}
