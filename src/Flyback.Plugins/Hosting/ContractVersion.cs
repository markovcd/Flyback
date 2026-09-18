using System.Reflection;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Hosting;

/// <summary>
/// Whether a plugin was compiled against a contract this host still offers,
/// asked before anything in the plugin is run.
/// </summary>
/// <remarks>
/// Asked of the assembly rather than of the plugin, because the compiler has
/// already written the answer down: every assembly records the version of each
/// one it was built against. So a plugin declares nothing, and one written
/// before there was a question to answer is still asked it.
/// <para>
/// Asked first because the runtime would not ask at all. A reference to an older
/// contract binds to the host's newer one without complaint, and a member that
/// has since gone is only missed when the method naming it is first compiled —
/// which for a module is the middle of compiling somebody's patch, and for a
/// sound backend is the audio thread.
/// </para>
/// </remarks>
internal static class ContractVersion
{
    /// <summary>The two assemblies a plugin is compiled against, and the version of each this host offers.</summary>
    private static readonly AssemblyName[] Offered =
    [
        typeof(IFlybackPlugin).Assembly.GetName(),
        typeof(NodeDef).Assembly.GetName(),
    ];

    /// <summary>
    /// Why <paramref name="plugin"/> cannot be loaded, or null where it can.
    /// </summary>
    public static string? Refusal(Assembly plugin)
    {
        foreach (var reference in plugin.GetReferencedAssemblies())
        {
            var offered = Offered.FirstOrDefault(o =>
                string.Equals(o.Name, reference.Name, StringComparison.OrdinalIgnoreCase));

            if (offered is not null && Refusal(reference, offered.Version!) is { } refusal) return refusal;
        }

        return null;
    }

    /// <summary>
    /// The same question about one reference. A different major is a contract
    /// with something taken away, in one direction or the other; a newer minor
    /// is one with something added that this host does not have.
    /// </summary>
    public static string? Refusal(AssemblyName reference, Version offered)
    {
        if (reference.Version is not { } built) return null;

        var (against, here) = ($"{reference.Name} {built.ToString(3)}", offered.ToString(3));

        if (built.Major < offered.Major)
            return $"ignored — built against {against}, and this Flyback offers {here}. The plugin needs rebuilding.";

        if (built.Major > offered.Major || built.Minor > offered.Minor)
            return $"ignored — built against {against}, and this Flyback only offers {here}. It needs a newer Flyback.";

        return null;
    }
}
