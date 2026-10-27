namespace Flyback.Editor.Shell;

/// <summary>What the editor is running in, and what it may reach outside itself.</summary>
/// <remarks>Left unset, it is a desktop window that reaches no network, tells the operating system nothing and offers no restart.</remarks>
public sealed record EditorHost
{
    /// <summary>
    /// The editor is in a browser page (ADR-0162): nothing is opened, saved or recorded,
    /// there is no assistant, plugins or About, settings are a few in a panel of their own,
    /// and the picture stays put, drawn at 480 x 270.
    /// </summary>
    public bool InPage { get; init; }

    /// <summary>The site the gallery lists shared presets from and the plugins window shared plugins.</summary>
    public Uri? PresetSite { get; init; }

    /// <summary>What the Files section tells the operating system.</summary>
    public FileTypes? FileTypes { get; init; }

    /// <summary>
    /// Starts Flyback again once this window has closed, which is what loads a plugin
    /// just installed. Null offers no restart.
    /// </summary>
    public Action<Reopen?>? Relaunch { get; init; }

    /// <summary>
    /// Gives the whole screen to the editor, or takes it back, as the picture goes full screen,
    /// for a host whose screen is not the window's to change. Null changes the window's state instead.
    /// </summary>
    public Action<bool>? WholeScreen { get; init; }

    /// <summary>
    /// Moves the picture behind the editor's surface, or back above it, for a host that draws
    /// the picture itself above that surface, a page's canvas. Null where the editor draws it.
    /// </summary>
    public Action<bool>? PictureBehind { get; init; }

    /// <summary>Leaves the editor for the front page of the site it is served from. Null puts no mark on the toolbar.</summary>
    public Action? Home { get; init; }
}
