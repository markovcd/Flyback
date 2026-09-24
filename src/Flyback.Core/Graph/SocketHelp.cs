namespace Flyback.Core.Graph;

/// <summary>
/// The sockets that mean the same wherever they appear, described once here rather
/// than on every module that has one.
/// </summary>
/// <remarks>
/// A socket with no <see cref="PortSpec.Help"/> of its own takes the standard help for
/// its name; one whose name means something else on its module says so in its own
/// help, which wins. The assistant is told the standard list once and a module's
/// own help only where it differs, while the inspector shows whichever applies.
/// </remarks>
public static class SocketHelp
{
    /// <summary>
    /// What a domain input is for, whatever it is called: the axis a module runs
    /// across, normalled to Time.
    /// </summary>
    public const string Domain =
        "What it runs across: Time without a wire, so it moves. A coordinate lays it across the screen instead.";

    /// <summary>The standard inputs, by name.</summary>
    public static IReadOnlyDictionary<string, string> Inputs { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["x"] = "Where on the picture it is read: the pixel's own without a wire. A Geometry module in between moves, turns or bends it.",
        ["y"] = "Where on the picture it is read: the pixel's own without a wire. A Geometry module in between moves, turns or bends it.",
        ["freq"] = "Cycles for each unit of 'in': hertz while it runs on Time.",
        ["phase"] = "Where in the cycle it starts. 1 is once round, so it wraps.",
        ["amp"] = "Multiplies what comes out, which swings -1 to 1 before it.",
        ["bias"] = "Added after 'amp', moving the whole output up or down.",
        ["right"] = "Carries 'left' when nothing is patched.",
        ["mix"] = "Dry against wet: 0 is a wire, 1 the effect alone.",
        ["depth"] = "How far the sweep swings.",
        ["seed"] = "Which random run it takes. Give each module its own; two with the same seed move together.",
        ["angle"] = "In radians.",
        ["t"] = "How far along: 0 is all 'a', 1 is all 'b'.",
        ["gate length"] = "How much of each step the gate stays open.",
    };

    /// <summary>The standard outputs, by name.</summary>
    public static IReadOnlyDictionary<string, string> Outputs { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["x"] = "The moved coordinate. Patch it into the 'x' and 'y' of the pattern to be moved.",
        ["y"] = "The moved coordinate. Patch it into the 'x' and 'y' of the pattern to be moved.",
        ["lfo"] = "The sweep itself, -1 to 1. It works on the picture too.",
    };

    /// <summary>What a socket of this name and kind is for wherever it appears, or empty where there is no standard.</summary>
    public static string Standard(PortSpec port, bool input) =>
        input && port.Domain
            ? Domain
            : (input ? Inputs : Outputs).GetValueOrDefault(port.Name, string.Empty);

    /// <summary>What the socket is for: its own help, or the standard for its name.</summary>
    public static string For(PortSpec port, bool input) => port.Help.Length > 0 ? port.Help : Standard(port, input);

    /// <summary>Whether the socket says something the standard list does not, and so has to be told on its module.</summary>
    public static bool Own(PortSpec port, bool input) => port.Help.Length > 0 && port.Help != Standard(port, input);
}
