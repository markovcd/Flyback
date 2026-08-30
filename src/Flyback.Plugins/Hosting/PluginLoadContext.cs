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
internal sealed class PluginLoadContext(string entryAssemblyPath)
    : AssemblyLoadContext(Path.GetFileNameWithoutExtension(entryAssemblyPath))
{
    /// <summary>
    /// Assemblies the host owns. These must resolve to the host's copy or the
    /// same type would have two identities and every cast across the boundary
    /// would fail — the classic plugin bug.
    /// </summary>
    /// <remarks>
    /// The same two are held out of the single-file bundle when the shell is
    /// published, so that a plugin can be compiled against the copies a given
    /// build shipped — see <c>Flyback.App.csproj</c>. That is a convenience and
    /// this is a correctness rule; they name the same pair because being the
    /// boundary is what makes both true.
    /// </remarks>
    private static readonly string[] HostOwned =
    [
        typeof(IFlybackPlugin).Assembly.GetName().Name!,
        $"{nameof(Flyback)}.{nameof(Core)}",
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
