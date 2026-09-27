using Avalonia.Platform.Storage;
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
    ReportLine report)
{
    /// <summary>Asks before replacing the document, then opens the picked file.</summary>
    public async Task PickAndOpenAsync()
    {
        if (await unsaved.MayReplaceThePatchAsync() && await files.PickOpenAsync() is { } file)
            await OpenFileAsync(file);
    }

    /// <summary>Resolves and opens a path supplied at launch.</summary>
    public async Task OpenPathAsync(string path)
    {
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

    /// <summary>Handles a file handed to the running program or dropped on its window.</summary>
    public async Task OpenActivatedFileAsync(IStorageFile file)
    {
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
        if (PluginPackage.Named(file.Name)) await installs.InstallAsync(file);
        else await files.OpenFileAsync(file);
    }
}
