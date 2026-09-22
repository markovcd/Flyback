using System.Diagnostics;
using Flyback.App.PluginPackages;
using Flyback.App.Updates;
using Flyback.Core;
using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Hosting;

namespace Flyback.App;

/// <summary>
/// Everything that has to happen before there is a window. Plugins are read
/// once here rather than by the window, because the module catalogue must be
/// final before a palette is built or a patch is opened — and because a second
/// window must never get a second answer.
/// </summary>
internal static class Startup
{
    public static PluginCatalog Plugins { get; private set; } = PluginCatalog.Empty;

    /// <summary>
    /// The file the program was started with — dragged onto its icon, or handed
    /// to it as the first argument on a command line — or null for an ordinary
    /// launch. Read here rather than by the window itself, since a plugin
    /// problem is announced before there is one to open it into and the two
    /// belong beside each other as the first things this program does.
    /// </summary>
    public static string? OpenPath { get; private set; }

    /// <summary>What <see cref="Interpreted"/> is asked for with on the command line.</summary>
    public const string InterpretedFlag = "--interpreted";

    /// <summary>
    /// Whether this run keeps the CPU's programs on the interpreter rather than
    /// building machine code under them (ADR-0076). A flag rather than a setting:
    /// the two give the same bits, so it is for comparing what they cost or
    /// ruling the compiled code out of a fault, never a preference to be kept.
    /// </summary>
    public static bool Interpreted { get; private set; }

    /// <summary>
    /// Whether this launch looks for a new release — read before there was anything
    /// else to read, since it decides whether this launch is this version's at all.
    /// </summary>
    public static UpdateSettings Updates { get; private set; } = new();

    /// <summary>
    /// What the last update did — that it installed, or why it could not — for the
    /// window to say once, or null where nothing was tried since the last launch.
    /// </summary>
    public static string? UpdateNote { get; private set; }

    /// <summary>
    /// What changed since the release that was replaced, where <see cref="UpdateNote"/> says it
    /// installed and its changelog has a section for it — shown in place of the note.
    /// </summary>
    public static ReleaseNotes? WhatsNew { get; private set; }

    /// <summary>
    /// Whether there was no settings folder when this launch began: the first start
    /// on this machine. Asked before anything here writes one.
    /// </summary>
    public static bool FirstRun { get; private set; }

    /// <summary>The plugins a package installed since the last launch, for the window to say once, or null.</summary>
    public static string? PluginNote { get; private set; }

    /// <summary>Whether a release Flyback downloaded itself installed just before this launch.</summary>
    public static bool Updated { get; private set; }

    public static void Load(string? openPath = null, bool interpreted = false, UpdateSettings? updates = null)
    {
        FirstRun = !Directory.Exists(GlobalConstants.DataFolder);

        OpenPath = openPath;
        Interpreted = interpreted;
        Updates = updates ?? new UpdateSettings();

        // Before the plugins, so a version that has just been replaced by the
        // one running is cleared away while nothing is reading it.
        var running = ReleaseFeed.Running();

        (UpdateNote, var replaced) = Updater.Folder.Tidy(running);

        Updated = running is not null && UpdateNote == Updater.Installed(running);

        if (Updated) WhatsNew = ReleaseNotes.Of(running!, since: replaced);

        if (UpdateNote is not null) Trace.WriteLine($"updates: {UpdateNote}");

        // Before the scan, which is the first moment nothing has a plugin open.
        var (installed, refused) = PluginInstaller.Finish(PluginHost.DefaultDirectory);

        if (installed.Count > 0) PluginNote = $"Installed {string.Join(", ", installed)}.";

        foreach (var problem in refused) Trace.WriteLine($"plugins: {problem}");

        Plugins = PluginHost.Load();
        NodeCatalog.Install(Plugins.Modules);

        // Here rather than when the assistant is first asked, so the file the
        // settings point at is there to open before anybody has used it.
        PriorityModules.Install();

        Announce(Plugins);
    }

    /// <summary>
    /// What the scan found, on the terminal.
    /// </summary>
    /// <remarks>
    /// The window says as much in a tooltip, which is no use to somebody who started
    /// the program from a shell to find out why their plugin is missing — and a plugin
    /// that failed to load failed before there was a window. Where it looked is said
    /// whatever the answer was, because an empty folder and the wrong folder read
    /// identically from a list of nothing.
    /// </remarks>
    private static void Announce(PluginCatalog catalog)
    {
        Trace.WriteLine($"plugins: {PluginHost.DefaultDirectory}");

        if (catalog.Plugins.Count == 0) Trace.WriteLine("  nothing loaded");

        foreach (var plugin in catalog.Plugins)
            Trace.WriteLine($"  loaded {plugin.Info.Name}  ({plugin.Info.Id})");

        foreach (var problem in catalog.Problems)
            Trace.WriteLine($"  problem: {problem}");
    }
}
