namespace Flyback.Core.Graph;

/// <summary>What flows down a wire.</summary>
public enum PortKind
{
    /// <summary>A single value that varies over x, y and t — the audio-rate signal of a video synth.</summary>
    Scalar,

    /// <summary>Three signals travelling together as red, green and blue.</summary>
    Colour,

    /// <summary>
    /// Whatever is plugged in, passed through unchanged. Maths modules use this
    /// so a single Multiply works on both a scalar and a colour, the way a
    /// shading language overloads its operators.
    /// </summary>
    Any,
}

/// <summary>
/// One input or output socket on a node. Inputs carry a <see cref="Default"/>
/// that is editable on the node itself, so most patches need no constant nodes.
/// </summary>
/// <param name="Name">Label shown next to the socket.</param>
/// <param name="Kind">Scalar or colour.</param>
/// <param name="Default">Value used when nothing is plugged in.</param>
/// <param name="Min">Lower end of the slider range in the editor.</param>
/// <param name="Max">Upper end of the slider range in the editor.</param>
public readonly record struct PortSpec(
    string Name,
    PortKind Kind = PortKind.Scalar,
    float Default = 0f,
    float Min = -4f,
    float Max = 4f)
{
    public int Width => Kind == PortKind.Colour ? 3 : 1;
}
