using Avalonia.Platform.Storage;
using Flyback.App.Capture;
using Flyback.App.Controls;
using Flyback.App.PluginPackages;
using Flyback.Plugins.Hosting;

namespace Flyback.App.Files;

/// <summary>Coordinates the routes that open or install a file (ADR-0148).</summary>
internal sealed class PatchOpening(
    PatchFiles files,
    UnsavedWork unsaved,
    PluginInstalls installs,
    IFilePickers pickers,
    IDialog dialog,
    ReportLine report,
    RecordingState recording)
{
    /// <summary>Asks before replacing the document, then opens the picked file.</summary>
    public async Task PickAndOpenAsync()
    {
        if (RefuseWhileRecording()) return;

        if (await unsaved.MayReplaceThePatchAsync() && await files.PickOpenAsync() is { } file)
            await OpenFileAsync(file);
    }

    /// <summary>Resolves and opens a path supplied at launch.</summary>
    public async Task OpenPathAsync(string path)
    {
        if (RefuseWhileRecording()) return;

        if (!PluginPackage.Named(path) && !await unsaved.MayReplaceThePatchAsync()) return;

        IStorageFile? file;

        try
        {
            file = await pickers.FromPath(path);
        }
        catch (Exception ex)
        {
            report.Say($"Could not open {Path.GetFileName(path)}: {ex.Message}");
            return;
        }

        if (file is null)
        {
            report.Say($"Could not open {Path.GetFileName(path)}.");
            return;
        }

        await OpenFileAsync(file);
    }

    /// <summary>
    /// Opens a file handed to the program from outside a picker or a drop —
    /// which on macOS is how "open this file" arrives at all: Finder delivers
    /// it as an activation rather than as a command-line argument, whether
    /// that launches the program or lands on its Dock icon while it is
    /// already running. See <see cref="FlybackApp.OnFrameworkInitializationCompleted"/>.
    /// </summary>
    /// <remarks>
    /// Not while a dialog is up. A dialog stops the pointer and the keyboard and
    /// neither of these arrives by them, so with nothing unsaved to ask about the
    /// document would be replaced behind the sheet — and a text one takes the
    /// focus with it, out of a dialog that then no longer hears Escape.
    /// </remarks>
    public async Task OpenActivatedFileAsync(IStorageFile file)
    {
        if (RefuseWhileRecording()) return;

        if (dialog.IsShowing)
        {
            report.Say($"{file.Name} was not opened: there is a dialog to answer first.");
            return;
        }

        if (PluginPackage.Named(file.Name) || await unsaved.MayReplaceThePatchAsync())
            await OpenFileAsync(file);
    }

    private async Task OpenFileAsync(IStorageFile file)
    {
        if (RefuseWhileRecording()) return;

        if (PluginPackage.Named(file.Name)) await installs.InstallAsync(file);
        else await files.OpenFileAsync(file);
    }

    private bool RefuseWhileRecording()
    {
        if (!recording.Running) return false;

        report.Say("Stop the recording before opening a file.");
        return true;
    }
}
