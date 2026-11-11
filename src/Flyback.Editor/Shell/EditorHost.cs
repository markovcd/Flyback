namespace Flyback.Editor.Shell;

/// <summary>What the editor is running in, and what it may reach outside itself.</summary>
/// <remarks>Left unset, it is a desktop window that reaches no network, tells the operating system nothing and offers no restart.</remarks>
public sealed record EditorHost
{
    /// <summary>Where the editor runs. A feature asks the capability below that it needs, not this.</summary>
    public EditorPlace Place { get; init; }

    /// <summary>In a browser page: one thread, WebGL, the browser's storage and no file system.</summary>
    internal bool InPage => Place == EditorPlace.Page;

    /// <summary>On a phone or tablet: one screen, and no other program to start or hand files to.</summary>
    internal bool OnDevice => Place == EditorPlace.Device;

    /// <summary>Open and Save, and the offer to save before work is lost.</summary>
    internal bool OpensFiles => Place != EditorPlace.Page;

    /// <summary>Record, which writes a take to a file, and its settings: only a desktop offers them.</summary>
    internal bool Records => Place == EditorPlace.Desktop;

    /// <summary>Measure, which only a desktop offers: a page's one thread would hang on it.</summary>
    internal bool Measures => Place == EditorPlace.Desktop;

    /// <summary>The assistant.</summary>
    internal bool Assists => Place != EditorPlace.Page;

    /// <summary>The About box.</summary>
    internal bool ShowsAbout => Place != EditorPlace.Page;

    /// <summary>The plugins window, which a device lacks because its plugins are linked in.</summary>
    internal bool InstallsPlugins => Place == EditorPlace.Desktop;

    /// <summary>The settings window; a page has a few settings in a panel of their own instead.</summary>
    internal bool SettingsWindow => Place != EditorPlace.Page;

    /// <summary>The View it button, which leaves the editor for the page's player.</summary>
    internal bool OffersViewer => Place == EditorPlace.Page;

    /// <summary>The site the gallery lists shared presets from and the plugins window shared plugins.</summary>
    public Uri? PresetSite { get; init; }

    /// <summary>What the Files section tells the operating system.</summary>
    public FileTypes? FileTypes { get; init; }


    /// <summary>
    /// Flyback updates itself here (ADR-0088), so Settings offers the switch for it. Unset where
    /// nothing in Flyback does: a phone, a page, a Microsoft Store copy.
    /// </summary>
    public bool UpdatesItself { get; init; }

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

    /// <summary>
    /// A scroll pans the canvas and a pinch or Ctrl/Cmd and the wheel zooms it, as on a Mac, where
    /// the wheel is two fingers on a trackpad. Unset, the wheel zooms.
    /// </summary>
    public bool ScrollPans { get; init; }

    /// <summary>
    /// Whether the scroll being handled comes from a mouse wheel (true) or a trackpad (false), where
    /// <see cref="ScrollPans"/> is set: a mouse wheel then zooms (ADR-0226). Null, or answering null, pans.
    /// </summary>
    public Func<bool?>? ScrollIsMouse { get; init; }

    /// <summary>Leaves the editor for the front page of the site it is served from. Null puts no mark on the toolbar.</summary>
    public Action? Home { get; init; }
}
