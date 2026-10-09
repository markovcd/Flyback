using System.Reflection;

namespace Flyback.Plugins.Hosting;

/// <summary>
/// Finds and loads plugins. One scan, at startup — there is no reload, because an
/// assembly the audio thread is calling into cannot be unloaded safely.
/// </summary>
/// <remarks>
/// One folder per plugin under <c>plugins/</c>, each holding the plugin assembly,
/// its <c>.deps.json</c> and its private dependencies. Nothing here throws: a
/// plugin that is missing, broken, built against another runtime or against a
/// contract this host does not offer (<see cref="ContractVersion"/>), or simply
/// hostile is a line in <see cref="PluginCatalog.Problems"/>. So is a folder nobody
/// allowed (<see cref="PluginTrust"/>), which is not loaded at all.
/// </remarks>
internal static class PluginHost
{
    public const string DirectoryName = "plugins";

    /// <summary>The <c>plugins</c> folder beside the executable.</summary>
    public static string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, DirectoryName);

    public static PluginCatalog Load() => Load(DefaultDirectory, PluginTrust.For(DefaultDirectory));

    public static PluginCatalog Load(string directory, PluginTrust trust)
    {
        if (!Directory.Exists(directory)) return PluginCatalog.Empty;

        var catalog = new PluginCatalogBuilder();

        foreach (var folder in Folders(directory)) LoadFolder(folder, catalog, trust);

        return catalog.Build();
    }

    /// <summary>
    /// Loads the plugins in one folder alone and unloads them again, as <c>pack-plugin</c>
    /// tries a build: what went wrong, and how many plugins loaded.
    /// </summary>
    internal static (IReadOnlyList<PluginProblem> Problems, int Loaded) Try(string folder)
    {
        var contexts = new List<PluginLoadContext>();
        var result = Tried(folder, contexts);

        foreach (var context in contexts) context.Unload();

        for (var i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        return result;
    }

    /// <summary>Kept apart from <see cref="Try"/> so nothing it loaded is still referenced once it returns.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (IReadOnlyList<PluginProblem> Problems, int Loaded) Tried(string folder, List<PluginLoadContext> contexts)
    {
        var catalog = new PluginCatalogBuilder();

        LoadFolder(folder, catalog, PluginTrust.Unchecked, contexts);

        return (catalog.Problems, catalog.Loaded);
    }

    /// <summary>
    /// Loads the plugins <paramref name="host"/> was built with, as a page links them in:
    /// each named by an <c>AssemblyMetadata("Plugin", name)</c> the build writes for a
    /// <c>ProjectReference</c> marked <c>LinkedPlugin="true"</c>, or under another
    /// <paramref name="key"/> by a program that checks against them rather than runs them.
    /// </summary>
    internal static PluginCatalog LoadLinked(Assembly host, string key = "Plugin")
    {
        var catalog = new PluginCatalogBuilder();

        var names = host.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(a => a.Key == key && a.Value is not null)
            .Select(a => a.Value!)
            .Order(StringComparer.Ordinal);

        foreach (var name in names)
        {
            Assembly assembly;

            try
            {
                assembly = Assembly.Load(name);
            }
            catch (Exception ex)
            {
                catalog.Problem(new PluginProblem(name, ex.Message));
                continue;
            }

            foreach (var type in PluginTypes(assembly, catalog)) catalog.Add(type, assembly.Location);
        }

        return catalog.Build();
    }

    /// <summary>Loads plugin types already in this process, for the tests.</summary>
    internal static PluginCatalog LoadTypes(params Type[] types)
    {
        var catalog = new PluginCatalogBuilder();

        foreach (var type in types) catalog.Add(type, type.Assembly.Location);

        return catalog.Build();
    }

    /// <summary>
    /// The plugin folders in the order they load: the same on every run, so priority
    /// ties break the same way, with a package's plugins after every other, so an id
    /// one shares with a plugin shipped or copied in by hand is the package's to lose.
    /// A folder whose name starts with a dot is an installation in progress, never a plugin.
    /// </summary>
    internal static IEnumerable<string> Folders(string directory) => Directory.EnumerateDirectories(directory)
        .Where(folder => !Path.GetFileName(folder).StartsWith('.'))
        .OrderBy(folder => File.Exists(Path.Combine(folder, PluginPackage.MarkerName)))
        .ThenBy(folder => folder, StringComparer.Ordinal);

    private static void LoadFolder(
        string folder,
        PluginCatalogBuilder catalog,
        PluginTrust trust,
        List<PluginLoadContext>? collectible = null)
    {
        var entries = EntryAssemblies(folder);
        if (entries.Count == 0) return;

        var before = catalog.Problems.Count;
        var verdict = trust.Judge(folder);

        if (verdict.Loads)
        {
            catalog.Secrets = verdict.Secrets;
            LoadEntries(entries, catalog, collectible);
        }
        else
        {
            catalog.Problem(new PluginProblem(Path.GetFileName(folder), verdict.Reason!));
        }

        catalog.Blame(before, folder);
    }

    private static void LoadEntries(
        List<string> entries,
        PluginCatalogBuilder catalog,
        List<PluginLoadContext>? collectible)
    {
        // One context for the whole folder: its dependencies are shared by
        // everything in it, and isolation is wanted *between* plugins.
        var context = new PluginLoadContext(entries[0], collectible is not null);
        collectible?.Add(context);

        foreach (var entry in entries)
        {
            var assembly = TryLoad(context, entry, catalog);
            if (assembly is null) continue;

            // Before a type of it is looked at: loading has bound nothing yet, so
            // a plugin refused here has run none of its code and named none of ours.
            if (ContractVersion.Refusal(assembly) is { } refusal)
            {
                catalog.Problem(new PluginProblem(Path.GetFileName(entry), refusal));
                continue;
            }

            foreach (var type in PluginTypes(assembly, catalog)) catalog.Add(type, entry);
        }
    }

    /// <summary>
    /// A <c>.deps.json</c> beside an assembly is what <c>EnableDynamicLoading</c>
    /// produces, so it identifies the plugin among its own dependencies without a
    /// manifest to keep in step. A folder without one is scanned whole.
    /// </summary>
    /// <remarks>
    /// A copy of a host-owned assembly is skipped rather than treated as a
    /// candidate: it is easy to ship by accident, and its dependency file would
    /// otherwise be picked as the folder's.
    /// </remarks>
    internal static List<string> EntryAssemblies(string folder)
    {
        var dlls = Directory.GetFiles(folder, "*.dll")
            .Where(d => !PluginLoadContext.IsHostOwned(Path.GetFileNameWithoutExtension(d)))
            .ToList();

        var declared = dlls.Where(d => File.Exists(Path.ChangeExtension(d, ".deps.json"))).ToList();

        return declared.Count > 0 ? declared : dlls;
    }

    private static Assembly? TryLoad(PluginLoadContext context, string path, PluginCatalogBuilder catalog)
    {
        try
        {
            return context.LoadFromAssemblyPath(path);
        }
        catch (BadImageFormatException)
        {
            // A native library sitting next to the managed ones. Not a problem,
            // just not a plugin.
            return null;
        }
        catch (Exception ex)
        {
            catalog.Problem(new PluginProblem(Path.GetFileName(path), ex.Message));
            return null;
        }
    }

    private static IEnumerable<Type> PluginTypes(Assembly assembly, PluginCatalogBuilder catalog)
    {
        Type[] types;

        try
        {
            types = assembly.GetExportedTypes();
        }
        catch (Exception ex)
        {
            catalog.Problem(new PluginProblem(assembly.GetName().Name ?? "plugin", ex.Message));
            return [];
        }

        return types.Where(t =>
            t is { IsAbstract: false, IsInterface: false }
            && typeof(IFlybackPlugin).IsAssignableFrom(t)
            && t.GetConstructor(Type.EmptyTypes) is not null);
    }
}
