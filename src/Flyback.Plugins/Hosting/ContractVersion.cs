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

    /// <summary>Whether <paramref name="name"/> is one of the assemblies a plugin is compiled against.</summary>
    public static bool IsContract(string? name) =>
        Offered.Any(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Why <paramref name="plugin"/> cannot be loaded, or null where it can.
    /// </summary>
    public static string? Refusal(Assembly plugin) =>
        Reason(plugin.GetReferencedAssemblies()) is { } reason ? Ignored(reason) : null;

    /// <summary>The same question about one reference.</summary>
    public static string? Refusal(AssemblyName reference, Version offered) =>
        Reason(reference, offered) is { } reason ? Ignored(reason) : null;

    /// <summary>
    /// Why an assembly referencing <paramref name="references"/> could not be loaded,
    /// as a sentence of its own, or null where it could. Asked of a plugin before it
    /// is installed, when all there is to read is its metadata.
    /// </summary>
    public static string? Reason(IEnumerable<AssemblyName> references)
    {
        foreach (var reference in references)
        {
            var offered = Offered.FirstOrDefault(o =>
                string.Equals(o.Name, reference.Name, StringComparison.OrdinalIgnoreCase));

            if (offered is not null && Reason(reference, offered.Version!) is { } reason) return reason;
        }

        return null;
    }

    /// <summary>
    /// A different major is a contract with something taken away, in one direction
    /// or the other; a newer minor is one with something added that this host does
    /// not have.
    /// </summary>
    private static string? Reason(AssemblyName reference, Version offered)
    {
        if (reference.Version is not { } built) return null;

        var (against, here) = ($"{reference.Name} {built.ToString(3)}", offered.ToString(3));

        if (built.Major < offered.Major)
            return $"Built against {against}, and this Flyback offers {here}. The plugin needs rebuilding.";

        if (built.Major > offered.Major || built.Minor > offered.Minor)
            return $"Built against {against}, and this Flyback only offers {here}. It needs a newer Flyback.";

        return null;
    }

    private static string Ignored(string reason) => $"ignored — {char.ToLowerInvariant(reason[0])}{reason[1..]}";
}
