using Avalonia.Controls;

namespace Flyback.App.Settings;

/// <summary>One tab of the settings window (ADR-0082), and what it puts in force.</summary>
internal interface ISettingsSection
{
    /// <summary>The tab's heading.</summary>
    string Name { get; }

    /// <summary>The controls, lent to the window and kept between openings.</summary>
    Control View { get; }

    /// <summary>Puts what was last saved in force, once, as the editor is built.</summary>
    void Start() { }

    /// <summary>Looks up afresh whatever may have changed since the window last opened.</summary>
    void Opening() { }

    /// <summary>Puts what was last saved back on the controls, for a window closed without Save.</summary>
    void Show() { }

    /// <summary>Keeps what the controls hold, and puts it in force.</summary>
    void Save() { }
}
