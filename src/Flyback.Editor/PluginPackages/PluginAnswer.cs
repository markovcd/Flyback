namespace Flyback.App.PluginPackages;

/// <summary>What the install dialog was answered with. Closing it without an answer is <see cref="Cancel"/>.</summary>
internal enum PluginAnswer
{
    Cancel,
    Install,
    InstallAndRestart,
    Remove,

    /// <summary>Fetch the newer build the plugin site has, and ask about installing it.</summary>
    Download,
}