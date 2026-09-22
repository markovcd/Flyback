using System.Reflection;
using System.Runtime.Loader;

namespace Flyback.Plugins.Hosting;

/// <summary>
/// One load context per plugin folder, so two plugins may depend on different
/// versions of the same package without either winning.
/// </summary>
/// <remarks>
/// <see cref="AssemblyDependencyResolver"/> reads the plugin's own
/// <c>.deps.json</c>, which is why plugin projects set
/// <c>EnableDynamicLoading</c> — without it the file is not produced and
/// nothing but the entry assembly resolves.
/// </remarks>
internal sealed class PluginLoadContext(string entryAssemblyPath, bool collectible = false)
    : AssemblyLoadContext(Path.GetFileNameWithoutExtension(entryAssemblyPath), collectible)
{
    /// <summary>
    /// Assemblies the host owns. These must resolve to the host's copy or the same type
    /// would have two identities and every cast across the boundary would fail.
    /// </summary>
    /// <remarks>
    /// The first two are the boundary: what a plugin is compiled against, and the
    /// only two it should ever name. The engine is here for the plugin that names
    /// it anyway, and for a copy left in a plugin folder — nothing a plugin is
    /// handed comes from it, but what the host runs a patch with must be one thing.
    /// </remarks>
    private static readonly string[] HostOwned =
    [
        typeof(IFlybackPlugin).Assembly.GetName().Name!,
        $"{nameof(Flyback)}.{nameof(Core)}",
        $"{nameof(Flyback)}.Engine",
    ];

    private readonly AssemblyDependencyResolver resolver = new(entryAssemblyPath);

    /// <summary>
    /// Whether the host, not the plugin, provides this assembly. Also used to
    /// ignore a stray copy in a plugin folder: loading one from there would
    /// give the contract a second identity, and a plugin built against it would
    /// silently stop being recognised as a plugin at all.
    /// </summary>
    internal static bool IsHostOwned(string? simpleName) =>
        simpleName is not null && HostOwned.Contains(simpleName, StringComparer.OrdinalIgnoreCase);

    protected override Assembly? Load(AssemblyName name)
    {
        if (IsHostOwned(name.Name)) return null;

        // Framework assemblies are absent from the plugin's deps.json, so they
        // resolve to null here and fall through to the default context too.
        var path = resolver.ResolveAssemblyToPath(name);

        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);

        return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }
}
