using Flyback.Plugins.Hosting;

namespace Flyback.App.PluginPackages;

/// <summary>A plugin a package installed, and who signed that package.</summary>
internal sealed record InstalledPlugin(PluginDescription Description, PackageSigner? Signer);