using System.Globalization;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Assist;

/// <summary>Finds a port a tool call names, and writes a number back the way every tool answers one.</summary>
internal static class ToolArguments
{
    public static bool Port(IReadOnlyList<PortSpec> ports, string name, out int index)
    {
        // An index is accepted as well as a name, which is the way out if a
        // plugin ever ships two ports called the same thing. Every listing a
        // tool prints shows both, so the escape hatch is always in view.
        if (int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var direct)
            && direct < ports.Count)
        {
            index = direct;
            return true;
        }

        for (var i = 0; i < ports.Count; i++)
        {
            if (!string.Equals(ports[i].Name, name, StringComparison.OrdinalIgnoreCase)) continue;

            index = i;
            return true;
        }

        index = -1;
        return false;
    }

    public static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
