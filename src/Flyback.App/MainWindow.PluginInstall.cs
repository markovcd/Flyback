using Avalonia.Platform.Storage;
using Flyback.App.Controls;
using Flyback.App.PluginPackages;

namespace Flyback.App;

/// <summary>Installing a plugin from a <c>.fbkp</c> opened with Flyback (ADR-0132).</summary>
public sealed partial class MainWindow
{
    /// <summary>Where a package's plugin is installed, or null where this window installs nothing.</summary>
    private readonly string? pluginFolder;

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
        var replacing = installer?.Replacing(package.Manifest.Id);

        if (!await this.ShowDialog<bool>(PluginInstallView.Title, PluginInstallView.View(package, platform, refusal, replacing))) return;

        var name = $"{package.Manifest.Name} {package.Manifest.Version}";

        try
        {
            installer!.Stage(package, platform);
            Report($"{name} is installed, and loads the next time Flyback starts.");
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Report($"{name} was not installed: {ex.Message}");
        }
    }
}
