using Flyback.Plugins.Hosting;

namespace Flyback.Editor.PluginPackages;

/// <summary>Creates installers for the plugins folder and plugins loaded this run.</summary>
internal sealed class PluginInstallerFactory(EditorFolders folders, PluginCatalog plugins)
{
    public PluginInstaller? Create() =>
        folders.PluginFolder is { } folder
            ? new PluginInstaller(folder, plugins.Plugins, allowances: folders.AllowedPluginsPath is { } allowed ? new PluginAllowances(allowed) : null)
            : null;
}
