namespace Flyback.App.PluginPackages;

/// <summary>What installing a package does to the plugin already in its folder.</summary>
internal enum PluginChange
{
    Install,
    Update,
    Reinstall,
    Downgrade,

    /// <summary>Replaces a plugin whose version cannot be put in order with the package's.</summary>
    Replace,
}