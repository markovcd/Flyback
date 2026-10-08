using Flyback.Plugins.Hosting;

namespace Flyback.Cli;

/// <summary>The plugin folder, scanned the first time a command needs it and not before.</summary>
/// <remarks>
/// A patch may name a module only a plugin defines, so everything that reads one asks
/// for the catalog before it does, and the scan is what the report on stderr is about.
/// Help, a completion, a refused command line, the two commands that only make a
/// plugin file and the ones that say which folders load or what a package holds need
/// no catalog: those load nothing and say nothing.
/// </remarks>
/// <param name="trust">Which folders load, for <c>plugin list</c>; <see cref="PluginTrust.For"/> the directory unless a test says otherwise.</param>
internal sealed class Plugins(Func<PluginCatalog> load, string directory, TextWriter? report, Func<PluginTrust>? trust = null)
{
    /// <summary>The plugins folder.</summary>
    public string Directory => directory;

    /// <summary>Which folders load, asked afresh.</summary>
    public PluginTrust Trust() => trust?.Invoke() ?? PluginTrust.For(directory);

    /// <summary>The catalog, scanning the folder if this is the first ask.</summary>
    public PluginCatalog Catalog => field ??= Scan();

    /// <summary>Asks for the catalog for its modules alone.</summary>
    public void Ready() => _ = Catalog;

    private PluginCatalog Scan()
    {
        // Before anything reads a patch: a catalog settled after the fact would
        // have let a file compile against the wrong module.
        var catalog = load().Install();

        if (report is not null)
            foreach (var line in PluginReport.Lines(catalog, directory)) report.WriteLine(line);

        return catalog;
    }
}
