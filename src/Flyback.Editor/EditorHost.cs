using Flyback.Editor.Files;

namespace Flyback.Editor;

/// <summary>What the editor is running in, and what it may reach outside itself.</summary>
/// <remarks>Left unset, it is a desktop window that reaches no network, tells the operating system nothing and offers no restart.</remarks>
public sealed record EditorHost
{
    /// <summary>
    /// The editor is in a browser page (ADR-0162): nothing is opened, saved or recorded,
    /// there is no assistant, settings, plugins or About, and the picture stays put, drawn at 480 x 270.
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

    /// <summary>Leaves the editor for the front page of the site it is served from. Null puts no mark on the toolbar.</summary>
    public Action? Home { get; init; }

    /// <summary>A desktop window on this machine, reaching the preset site this copy was built for.</summary>
    public static EditorHost ThisMachine() => new()
    {
        PresetSite = Site.PresetSite.Built,
        FileTypes = FileTypes.ForThisCopy(),
        Relaunch = Restart.Launch,
    };
}
