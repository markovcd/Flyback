using Flyback.Core.Graph;
using Flyback.Plugins.Hosting;

namespace Flyback.Cli;

/// <summary>The plugin folder, scanned the first time a command needs it and not before.</summary>
/// <remarks>
/// A patch may name a module only a plugin defines, so everything that reads one asks
/// for the catalog before it does, and the scan is what the report on stderr is about.
/// Help, a completion, a refused command line and the two commands that only make a
/// plugin file need no catalog: those load nothing and say nothing.
/// </remarks>
internal sealed class Plugins(Func<PluginCatalog> load, string directory, TextWriter? report)
{
    private PluginCatalog? loaded;

    /// <summary>The catalog, scanning the folder if this is the first ask.</summary>
    public PluginCatalog Catalog => loaded ??= Scan();

    /// <summary>Asks for the catalog for its modules alone.</summary>
    public void Ready() => _ = Catalog;

    private PluginCatalog Scan()
    {
        var catalog = load();

        // Before anything reads a patch: a catalog settled after the fact would
        // have let a file compile against the wrong module.
        NodeCatalog.Install(catalog.Modules);

        if (report is not null)
            foreach (var line in PluginReport.Lines(catalog, directory)) report.WriteLine(line);

        return catalog;
    }
}
