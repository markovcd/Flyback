namespace Flyback.Core.Graph;

/// <summary>
/// The sockets that mean the same wherever they appear, described once here rather
/// than on every module that has one.
/// </summary>
/// <remarks>
/// A socket opts in with <see cref="PortSpec.Standard"/>, and <see cref="NodeDef"/>
/// fills its help in from here by its name. Nothing is filled in unasked, so a socket
/// whose name means something else on its module cannot pick up words that are wrong
/// for it, and a socket that asks for a name with no standard is refused — see
/// <see cref="Missing"/>.
/// </remarks>
public static class SocketHelp
{
    /// <summary>
    /// What a domain input is for, whatever it is called: the axis a module runs
    /// across, normalled to Time.
    /// </summary>
    private const string Domain =
        "What it runs across: Time without a wire, so it moves. A coordinate lays it across the screen instead.";

    /// <summary>A position's x or y, normalled to Coordinates.</summary>
    private const string Position =
        "Where on the picture it is read: the pixel's own without a wire. A Geometry module in between moves, turns or bends it.";

    /// <summary>An oscillator's rate across its domain.</summary>
    private const string Freq = "Cycles for each unit of 'in': hertz while it runs on Time.";

    /// <summary>Where a cycle starts.</summary>
    private const string Phase = "Where in the cycle it starts. 1 is once round, so it wraps.";

    /// <summary>An oscillator's gain.</summary>
    private const string Amp = "Multiplies what comes out, which swings -1 to 1 before it.";

    /// <summary>An oscillator's offset.</summary>
    private const string Bias = "Added after 'amp', moving the whole output up or down.";

    /// <summary>A stereo module's right input, normalled from its left.</summary>
    private const string Right = "Carries 'left' when nothing is patched.";

    /// <summary>An effect's dry and wet balance.</summary>
    private const string Mix = "Dry against wet: 0 is a wire, 1 the effect alone.";

    /// <summary>A filter's resonance.</summary>
    private const string Resonance = "Peaks the corner, until it rings on a sharp edge.";

    /// <summary>An envelope's rise.</summary>
    private const string Attack = "How long the rise to full takes.";

    /// <summary>A sweep effect's depth.</summary>
    private const string Depth = "How far the sweep swings.";

    /// <summary>Which random run a module takes.</summary>
    private const string Seed = "Which random run it takes. Give each module its own; two with the same seed move together.";

    /// <summary>An angle.</summary>
    private const string Angle = "In radians.";

    /// <summary>A blend's position between 'a' and 'b'.</summary>
    private const string Blend = "How far along: 0 is all 'a', 1 is all 'b'.";

    /// <summary>A sequencer's gate length.</summary>
    private const string GateLength = "How much of each step the gate stays open.";

    /// <summary>A Geometry module's x or y out.</summary>
    private const string Moved = "The moved coordinate. Patch it into the 'x' and 'y' of the pattern to be moved.";

    /// <summary>A sweep effect's modulation out.</summary>
    private const string Lfo = "The sweep itself, -1 to 1. It works on the picture too.";

    /// <summary>A polar radius out.</summary>
    private const string Radius = "Distance from the center.";

    /// <summary>A shape's signed distance out.</summary>
    private const string Distance = "Negative inside, zero on the edge, positive outside.";

    /// <summary>The standard inputs, by name.</summary>
    public static IReadOnlyDictionary<string, string> Inputs { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["x"] = Position,
        ["y"] = Position,
        ["freq"] = Freq,
        ["phase"] = Phase,
        ["amp"] = Amp,
        ["bias"] = Bias,
        ["right"] = Right,
        ["mix"] = Mix,
        ["resonance"] = Resonance,
        ["attack"] = Attack,
        ["depth"] = Depth,
        ["seed"] = Seed,
        ["angle"] = Angle,
        ["t"] = Blend,
        ["gate length"] = GateLength,
    };

    /// <summary>The standard outputs, by name.</summary>
    public static IReadOnlyDictionary<string, string> Outputs { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["x"] = Moved,
        ["y"] = Moved,
        ["lfo"] = Lfo,
        ["radius"] = Radius,
        ["distance"] = Distance,
    };

    /// <summary>The standard for a socket of this name and side, or empty where there is none. Every domain input has one, whatever it is called.</summary>
    internal static string Standard(PortSpec port, bool input) =>
        input && port.Domain ? Domain : (input ? Inputs : Outputs).GetValueOrDefault(port.Name, string.Empty);

    /// <summary>The sockets, with the standard help filled in on each that asks for it.</summary>
    internal static IReadOnlyList<PortSpec> Filled(IReadOnlyList<PortSpec> ports, bool input) =>
        ports.Any(port => port.Standard)
            ? [.. ports.Select(port => port.Standard ? port with { Help = Standard(port, input) } : port)]
            : ports;

    /// <summary>What is wrong with a module's standard sockets: one line for each that asks for a standard its name does not have.</summary>
    internal static IEnumerable<string> Missing(NodeDef def) =>
        def.Inputs.Concat(def.Outputs)
            .Where(port => port.Standard && port.Help.Length == 0)
            .Select(port => $"'{def.TypeId}' asks for the standard help of '{port.Name}', and there is none by that name.");
}
