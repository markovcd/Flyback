namespace Flyback.Core.Graph;

/// <summary>
/// The sockets that mean the same wherever they appear, described once here rather
/// than on every module that has one.
/// </summary>
/// <remarks>
/// A socket opts in by taking one of these as its <see cref="PortSpec.Help"/>; nothing
/// is filled in by name, so a socket whose name means something else on its module
/// cannot pick up words that are wrong for it.
/// </remarks>
public static class SocketHelp
{
    /// <summary>
    /// What a domain input is for, whatever it is called: the axis a module runs
    /// across, normalled to Time.
    /// </summary>
    public const string Domain =
        "What it runs across: Time without a wire, so it moves. A coordinate lays it across the screen instead.";

    /// <summary>A position's x or y, normalled to Coordinates.</summary>
    public const string Position =
        "Where on the picture it is read: the pixel's own without a wire. A Geometry module in between moves, turns or bends it.";

    /// <summary>An oscillator's rate across its domain.</summary>
    public const string Freq = "Cycles for each unit of 'in': hertz while it runs on Time.";

    /// <summary>Where a cycle starts.</summary>
    public const string Phase = "Where in the cycle it starts. 1 is once round, so it wraps.";

    /// <summary>An oscillator's gain.</summary>
    public const string Amp = "Multiplies what comes out, which swings -1 to 1 before it.";

    /// <summary>An oscillator's offset.</summary>
    public const string Bias = "Added after 'amp', moving the whole output up or down.";

    /// <summary>A stereo module's right input, normalled from its left.</summary>
    public const string Right = "Carries 'left' when nothing is patched.";

    /// <summary>An effect's dry and wet balance.</summary>
    public const string Mix = "Dry against wet: 0 is a wire, 1 the effect alone.";

    /// <summary>A filter's resonance.</summary>
    public const string Resonance = "Peaks the corner, until it rings on a sharp edge.";

    /// <summary>An envelope's rise.</summary>
    public const string Attack = "How long the rise to full takes.";

    /// <summary>A sweep effect's depth.</summary>
    public const string Depth = "How far the sweep swings.";

    /// <summary>Which random run a module takes.</summary>
    public const string Seed = "Which random run it takes. Give each module its own; two with the same seed move together.";

    /// <summary>An angle.</summary>
    public const string Angle = "In radians.";

    /// <summary>A blend's position between 'a' and 'b'.</summary>
    public const string Blend = "How far along: 0 is all 'a', 1 is all 'b'.";

    /// <summary>A sequencer's gate length.</summary>
    public const string GateLength = "How much of each step the gate stays open.";

    /// <summary>A Geometry module's x or y out.</summary>
    public const string Moved = "The moved coordinate. Patch it into the 'x' and 'y' of the pattern to be moved.";

    /// <summary>A sweep effect's modulation out.</summary>
    public const string Lfo = "The sweep itself, -1 to 1. It works on the picture too.";

    /// <summary>A polar radius out.</summary>
    public const string Radius = "Distance from the center.";

    /// <summary>A shape's signed distance out.</summary>
    public const string Distance = "Negative inside, zero on the edge, positive outside.";
}
