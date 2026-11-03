using Avalonia.Controls;

namespace Flyback.Editor.Settings;

/// <summary>One tab of the settings window (ADR-0082), and what it puts in force.</summary>
internal interface ISettingsSection
{
    /// <summary>The tab's heading.</summary>
    string Name { get; }

    /// <summary>Which of <see cref="SettingsGroups"/> the tab is listed under.</summary>
    string Group { get; }

    /// <summary>The controls, lent to the window and kept between openings.</summary>
    Control View { get; }

    /// <summary>
    /// What the controls hold that only a restart puts in force and is not saved yet, as
    /// the end of "Restart Flyback to …", or null where nothing waits for one.
    /// </summary>
    string? Restarts => null;

    /// <summary>Puts what was last saved in force, once, as the editor is built.</summary>
    void Start() { }

    /// <summary>Looks up afresh whatever may have changed since the window last opened.</summary>
    void Opening() { }

    /// <summary>Puts what was last saved back on the controls, for a window closed without Save.</summary>
    void Show() { }

    /// <summary>Keeps what the controls hold, and puts it in force.</summary>
    void Save() { }
}
