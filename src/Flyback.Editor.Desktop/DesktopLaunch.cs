using Flyback.Editor.Updates;
using Flyback.Plugins.Hosting;

namespace Flyback.Editor.Desktop;

/// <summary>What <see cref="Startup.Load"/> found before there was a window, handed to the app that opens one.</summary>
internal sealed record DesktopLaunch
{
    /// <summary>What the editor is asked to do and has to say once it opens.</summary>
    public EditorLaunch Editor { get; init; } = new();

    /// <summary>Read once, before any window, so the module catalog is final before a palette is built.</summary>
    public PluginCatalog Plugins { get; init; } = PluginCatalog.Empty;

    /// <summary>Whether this launch looks for a new release.</summary>
    public UpdateSettings Updates { get; init; } = new();

    /// <summary>Whether there was no settings folder when this launch began: the first start on this machine.</summary>
    public bool FirstRun { get; init; }

    /// <summary>Whether a release Flyback downloaded itself installed just before this launch.</summary>
    public bool Updated { get; init; }
}
