namespace Flyback.App.PluginPackages;

/// <summary>A plugin this Flyback has, and what state it is in.</summary>
/// <param name="Waiting">The version waiting to replace it at the next start, or null where nothing is.</param>
/// <param name="Picture">Its preview, as the image file it carries.</param>
/// <param name="Removing">Whether it is removed at the next start.</param>
/// <param name="Assisting">Where Ask sends a patch, where this plugin is what it sends to, or null where it is not.</param>
/// <param name="Trouble">What went wrong with it this run, or null where nothing did.</param>
internal sealed record HubInstalled(
    ListedPlugin Plugin, string? Waiting, bool Loaded, byte[]? Picture, bool Removing = false, string? Assisting = null, string? Trouble = null)
{
    /// <summary>The version that will be running after the next start.</summary>
    public string Version => Waiting ?? Plugin.Version;

    /// <summary>Whether it is loaded, or what waits for the next start.</summary>
    public string State => Removing ? "Removed at the next start"
        : Waiting is { } version
            ? Loaded ? $"{version} loads at the next start" : "Loads at the next start"
            : Loaded ? "Loaded" : "Not loaded";
}