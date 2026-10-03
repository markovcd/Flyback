namespace Flyback.Core.Graph;

/// <summary>
/// A collapsed group's whole interface: every port at which a wire crosses in,
/// and every port from which one leaves.
/// </summary>
/// <remarks>
/// Derived from the wires alone, so an input resting on a knob is not here and
/// neither is one normalled to Time (ADR-0050). The two sides are not symmetric:
/// an input takes at most one wire, so a crossing-in wire is one socket that
/// never shares, where several wires leaving one inner output are one socket with
/// several wires on it.
/// </remarks>
public readonly record struct GroupSockets(
    IReadOnlyList<GroupSocket> Inputs,
    IReadOnlyList<GroupSocket> Outputs)
{
    /// <summary>How many sockets the box shows, inputs and outputs together.</summary>
    public int Rows => Inputs.Count + Outputs.Count;

    /// <summary>Where <paramref name="socket"/> sits among the inputs, or -1.</summary>
    public int IndexOfInput(GroupSocket socket)
    {
        for (var i = 0; i < Inputs.Count; i++)
            if (Inputs[i] == socket) return i;

        return -1;
    }

    /// <inheritdoc cref="IndexOfInput"/>
    public int IndexOfOutput(GroupSocket socket)
    {
        for (var i = 0; i < Outputs.Count; i++)
            if (Outputs[i] == socket) return i;

        return -1;
    }
}