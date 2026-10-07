using Flyback.Core.Graph;

namespace Flyback.Engine.Language;

/// <summary>
/// A socket by name, with a space in the catalog's spelling standing for an
/// underscore in the language's: <c>gate length</c> is <c>gate_length</c>.
/// </summary>
internal static class SocketNames
{
    public static int Find(IReadOnlyList<PortSpec> ports, string name)
    {
        for (var i = 0; i < ports.Count; i++)
            if (Same(ports[i].Name, name)) return i;

        return -1;
    }

    public static bool Same(string port, string written) =>
        string.Equals(port.Replace(' ', '_'), written, StringComparison.OrdinalIgnoreCase);

    /// <summary>The sockets as the text would say them, for a complaint that lists what there is.</summary>
    public static string List(IReadOnlyList<PortSpec> ports) =>
        ports.Count == 0 ? "none" : string.Join(", ", ports.Select(p => $"'{p.Name.Replace(' ', '_')}'"));

    /// <summary>One socket as the text would say it.</summary>
    public static string Written(PortSpec port) => port.Name.Replace(' ', '_');
}
