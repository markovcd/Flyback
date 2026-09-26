using Flyback.Plugins.Hosting;

namespace Flyback.Server;

/// <summary>A submitted plugin package, with what its assemblies say about it.</summary>
/// <param name="Contract">Each contract assembly the plugin was compiled against, and its version.</param>
/// <param name="Modules">The modules its builds declare, each once.</param>
internal sealed record PluginSubmission(
    string Assembly,
    string Name,
    string Version,
    string Author,
    string Description,
    IReadOnlyList<string> Tags,
    PluginPreview? Preview,
    IReadOnlyList<string> Adds,
    IReadOnlyList<string> Reaches,
    IReadOnlyList<string> Builds,
    IReadOnlyDictionary<string, string> Contract,
    IReadOnlyList<DeclaredModule> Modules,
    string Sha256,
    PackageSigner? Signer,
    byte[] File)
{
    /// <summary>Named after the plugin rather than whatever the upload was called.</summary>
    public string FileName => Assembly + PluginPackage.Extension;
}