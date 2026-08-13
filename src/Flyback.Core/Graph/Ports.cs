using System.Globalization;

namespace Flyback.Core.Graph;

/// <summary>
/// How an input's value should be read back. This is presentation only — the
/// number compiles the same either way — and lives beside <see cref="PortSpec.Min"/>
/// and <see cref="PortSpec.Max"/>, which are already the editor's business
/// rather than the compiler's.
/// </summary>
public enum PortDisplay
{
    /// <summary>A plain number.</summary>
    Number,

    /// <summary>A note number, shown by name: 57 reads as "A3".</summary>
    Note,
}

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
/// <param name="NormalledFrom">
/// Index of an earlier input this one falls back to when nothing is patched in,
/// or -1 to fall back to <paramref name="Default"/>. This is the hardware
/// normalled jack: on a real rig, leaving the right channel unpatched carries
/// the left signal through rather than silence.
/// </param>
/// <param name="Display">How the editor should write the value out.</param>
public readonly record struct PortSpec(
    string Name,
    PortKind Kind = PortKind.Scalar,
    float Default = 0f,
    float Min = -4f,
    float Max = 4f,
    int NormalledFrom = -1,
    PortDisplay Display = PortDisplay.Number)
{
    public int Width => Kind == PortKind.Colour ? 3 : 1;

    /// <summary>
    /// The value as it should be shown for this socket. One place, because the
    /// node on the canvas and the row in the inspector have to agree about what
    /// a knob currently says.
    /// </summary>
    public string Format(float value) => Display == PortDisplay.Note
        ? Pitch.Name(value)
        : value.ToString("0.###", CultureInfo.InvariantCulture);
}
