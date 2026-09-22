using Avalonia.Platform.Storage;
using Flyback.App.Controls;
using Flyback.App.PluginPackages;
using Flyback.Plugins.Hosting;

namespace Flyback.App;

/// <summary>Installing or updating a plugin from a <c>.fbkp</c> opened with Flyback (ADR-0132).</summary>
public sealed partial class MainWindow
{
    /// <summary>Where a package's plugin is installed, or null where this window installs nothing.</summary>
    private readonly string? pluginFolder;

    /// <summary>Starts Flyback again once this window has closed, or null where a restart is not offered.</summary>
    private readonly Action? relaunch;

    /// <summary>
    /// Shows what the package says it is and installs it if asked. Leaves the patch
    /// alone, so nothing unsaved is asked about.
    /// </summary>
    private async Task InstallPluginAsync(IStorageFile file)
    {
        PluginPackage package;

        try
        {
            await using var stream = await file.OpenReadAsync();
            package = await PluginPackage.ReadAsync(stream);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Report($"{file.Name} was not installed. {ex.Message}");
            return;
        }

        var platform = PluginPackage.ThisPlatform;
        var installer = pluginFolder is null ? null : new PluginInstaller(pluginFolder, plugins.Plugins);
        var refusal = installer is null ? "This window has no plugins folder." : installer.Refusal(package, platform);
        var described = package.DescriptionFor(platform);
        var replacing = described is null ? null : installer?.Replacing(described.Assembly);
        var change = described is null ? PluginChange.Install : PluginChanges.Of(replacing?.Description, described);

        var view = PluginInstallView.View(package, platform, refusal, replacing, change, offerRestart: relaunch is not null);
        var answer = await this.ShowDialog<PluginAnswer>(PluginInstallView.Title(change), view);

        if (answer == PluginAnswer.Cancel) return;

        var name = $"{described!.Name} {described.Version}";

        try
        {
            installer!.Stage(package, platform);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Report($"{name} was not installed: {ex.Message}");
            return;
        }

        if (answer == PluginAnswer.InstallAndRestart && await RestartAsync()) return;

        Report($"{name} is {(change == PluginChange.Update ? "updated" : "installed")}, and loads the next time Flyback starts.");
    }

    /// <summary>
    /// Closes the window, asking about unsaved work as any close does, and starts
    /// Flyback again behind it. False where the window stays: the question was
    /// cancelled, or a recording is running, which only its own button should end.
    /// </summary>
    private async Task<bool> RestartAsync()
    {
        if (TakeInHand || !await MayReplaceThePatchAsync()) return false;

        relaunch!();

        leaving = true;
        Close();

        return true;
    }
}
