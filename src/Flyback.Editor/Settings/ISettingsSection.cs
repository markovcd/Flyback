using Avalonia.Controls;

namespace Flyback.App.Settings;

/// <summary>One tab of the settings window (ADR-0082).</summary>
/// <remarks>
/// What a section keeps in <c>output.json</c> is read and shown through
/// <see cref="IOutputSlice"/> instead, all of it at once, because the file is
/// applied whole.
/// </remarks>
internal interface ISettingsSection
{
    /// <summary>The tab's heading.</summary>
    string Name { get; }

    /// <summary>The controls, lent to the window and kept between openings.</summary>
    Control View { get; }

    /// <summary>Looks up afresh whatever may have changed since the window last opened.</summary>
    void Opening() { }

    /// <summary>Puts what was last saved back on the controls, for a window closed without Save.</summary>
    void Show() { }

    /// <summary>Keeps what the controls hold.</summary>
    void Save() { }
}
