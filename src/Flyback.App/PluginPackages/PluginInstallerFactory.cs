using Flyback.Plugins.Hosting;

namespace Flyback.App.PluginPackages;

/// <summary>Creates installers for the plugins folder and plugins loaded this run.</summary>
internal sealed class PluginInstallerFactory(EditorSetup setup, PluginCatalog plugins)
{
    public PluginInstaller? Create() =>
        setup.PluginFolder is { } folder
            ? new PluginInstaller(folder, plugins.Plugins, allowances: setup.AllowedPluginsPath is { } allowed ? new PluginAllowances(allowed) : null)
            : null;
}
