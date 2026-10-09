using System.Diagnostics;
using Flyback.Editor.PluginPackages;
using Flyback.Editor.Desktop.Updates;
using Flyback.Editor.Updates;
using Flyback.Core;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Hosting;

namespace Flyback.Editor.Desktop;

/// <summary>
/// Everything that has to happen before there is a window. Plugins are read
/// once here rather than by the window, because the module catalog must be
/// final before a palette is built or a patch is opened — and because a second
/// window must never get a second answer.
/// </summary>
internal static class Startup
{
    /// <param name="openPath">
    /// The file the program was started with — dragged onto its icon, or handed to it
    /// as the first argument on a command line — or null for an ordinary launch.
    /// </param>
    /// <param name="interpreted">
    /// Whether this run keeps the CPU's programs on the interpreter rather than
    /// building machine code under them (ADR-0076): for comparing what they cost or
    /// ruling the compiled code out of a fault, never a preference to be kept.
    /// </param>
    /// <param name="updates">Whether this launch looks for a new release, read before anything else.</param>
    /// <param name="shared">
    /// The shared preset to open again, where a restart to install a plugin was asked
    /// for while one was open: a preset from the site has no file to be named by.
    /// </param>
    public static DesktopLaunch Load(
        string? openPath = null,
        bool interpreted = false,
        UpdateSettings? updates = null,
        string? shared = null)
    {
        var firstRun = !Directory.Exists(GlobalConstants.DataFolder);

        // Before the plugins, so a version that has just been replaced by the
        // one running is cleared away while nothing is reading it.
        var running = ReleaseFeed.Running();

        var (updateNote, replaced) = Updater.Folder.Tidy(running);

        var updated = running is not null && updateNote == Updater.Installed(running);

        var whatsNew = updated && Changelog() is { } changelog ? ReleaseNotes.Of(running!, replaced, changelog) : null;

        if (updateNote is not null) Trace.WriteLine($"updates: {updateNote}");

        // Before the scan, which is the first moment nothing has a plugin open.
        var (installed, removed, refused) = PluginInstaller.Finish(PluginHost.DefaultDirectory);

        string?[] said =
        [
            installed.Count > 0 ? $"Installed {string.Join(", ", installed)}." : null,
            removed.Count > 0 ? $"Removed {string.Join(", ", removed)}." : null,
        ];

        var pluginNote = said.Any(s => s is not null) ? string.Join(' ', said.OfType<string>()) : null;

        foreach (var problem in refused) Trace.WriteLine($"plugins: {problem}");

        var plugins = PluginHost.Load().Install();

        // Here rather than when the assistant is first asked, so the file the
        // settings point at is there to open before anybody has used it.
        PriorityModules.Install();

        // The window says as much in a tooltip, which is no use to somebody who started
        // the program from a shell to find out why their plugin is missing.
        foreach (var line in PluginReport.Lines(plugins, PluginHost.DefaultDirectory)) Trace.WriteLine(line);

        return new DesktopLaunch
        {
            Editor = new EditorLaunch
            {
                OpenPath = openPath,
                OpenShared = shared,
                Interpreted = interpreted,
                OpeningNote = updateNote is null ? pluginNote
                    : pluginNote is null ? updateNote
                    : $"{updateNote}  {pluginNote}",
                WhatsNew = whatsNew,
            },
            Plugins = plugins,
            Updates = updates ?? new UpdateSettings(),
            FirstRun = firstRun,
            Updated = updated,
        };
    }

    /// <summary>The changelog built into this program, or null where it cannot be read.</summary>
    internal static string? Changelog()
    {
        try
        {
            using var stream = typeof(Startup).Assembly.GetManifestResourceStream("CHANGELOG.md");

            if (stream is null) return null;

            using var reader = new StreamReader(stream);

            return reader.ReadToEnd();
        }
        catch (IOException)
        {
            return null;
        }
    }
}
