namespace Flyback.Core.Graph;

/// <summary>What flows down a wire.</summary>
public enum PortKind
{
    /// <summary>A single value that varies over x, y and t — the audio-rate signal of a video synth.</summary>
    Scalar,

    /// <summary>Three signals traveling together as red, green and blue.</summary>
    Color,

    /// <summary>
    /// Whatever is plugged in, passed through unchanged. Maths modules use this
    /// so a single Multiply works on both a scalar and a color, the way a
    /// shading language overloads its operators.
    /// </summary>
    Any,
}