using System.Globalization;

namespace Flyback.Core.Graph;

/// <summary>
/// How an input's value should be read back. The compiler never consults it —
/// any given number lowers the same whatever this says — so it lives beside
/// <see cref="PortSpec.Min"/> and <see cref="PortSpec.Max"/>, which are already
/// the editor's business rather than the compiler's.
/// </summary>
/// <remarks>
/// As well as writing a value out, it says whether the editor should let the
/// control rest between whole numbers — see <see cref="PortSpec.Stepped"/>.
/// That is still the editor's business in the same way a slider range is:
/// nothing here changes what a stored number means, and a signal arriving down
/// a wire is untouched by any of it.
/// </remarks>
public enum PortDisplay
{
    /// <summary>A plain number.</summary>
    Number,

    /// <summary>A note number, shown by name: 57 reads as "A3".</summary>
    Note,

    /// <summary>
    /// A length of time held as its power of ten, shown as the time it is: -3
    /// reads as "1 ms" and 0.3 as "2 s".
    /// </summary>
    /// <remarks>
    /// The same trick as <see cref="Note"/> — a number on the module's own scale,
    /// written out as what it stands for — and it is here for a reason a linear
    /// knob cannot answer. A probe's timebase runs from a fraction of one audio
    /// cycle to half a minute of an LFO, and no slider spans five decades: at a
    /// maximum of thirty seconds, every audio-rate setting is inside the first
    /// thousandth of the travel. In decades the whole range is one even sweep,
    /// which is exactly why the control on a scope is marked the way it is.
    /// </remarks>
    Duration,
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
/// <param name="Domain">
/// True when this input is the axis the module is read across rather than a
/// value it uses — an oscillator's phase source, a sequencer's position. What
/// makes it different from every other input is that a constant is never a
/// sensible thing to leave on it: an oscillator accumulates how far its domain
/// moved, so one that does not move produces a fixed value, and a sequencer sits
/// on whichever step its domain has reached. Neither is an error and both
/// compile perfectly, which is exactly why the compiler says so — see
/// <see cref="Compile.IssueSeverity.Warning"/>.
/// </param>
/// <param name="Swept">
/// True when the module reads this input over a domain of its own making rather
/// than over the pixel's. The compiler leaves such an input unresolved and hands
/// the module a way to resolve it itself, so that whatever it does to the domain
/// first — a Probe sweeping time across the picture — is in force by the time
/// everything upstream is lowered. See <see cref="EmitContext.Resolve"/>.
/// <para>
/// The opposite of <paramref name="Domain"/>, which names an input the module is
/// read <em>across</em>. This one names an input read <em>under</em> a domain the
/// module supplies, and it is the module rather than the port that says what
/// that domain is.
/// </para>
/// </param>
public readonly record struct PortSpec(
    string Name,
    PortKind Kind = PortKind.Scalar,
    float Default = 0f,
    float Min = -4f,
    float Max = 4f,
    int NormalledFrom = -1,
    PortDisplay Display = PortDisplay.Number,
    bool Domain = false,
    bool Swept = false)
{
    public int Width => Kind == PortKind.Colour ? 3 : 1;

    /// <summary>
    /// The value as it should be shown for this socket. One place, because the
    /// node on the canvas and the row in the inspector have to agree about what
    /// a knob currently says.
    /// </summary>
    /// <remarks>
    /// The case matches how the module reads the number, which is the whole
    /// point: <see cref="PortDisplay.Note"/> rounds because Note rounds.
    /// </remarks>
    public string Format(float value) => Display switch
    {
        PortDisplay.Note => Pitch.Name(value),
        PortDisplay.Duration => Time(value),
        _ => value.ToString("0.###", CultureInfo.InvariantCulture),
    };

    /// <summary>
    /// A power of ten of seconds, in the unit that leaves it readable — the same
    /// number every time, said in microseconds down at an audio cycle and in
    /// seconds up where an LFO lives.
    /// </summary>
    private static string Time(float decades)
    {
        var seconds = MathF.Pow(10f, decades);

        // Anything a patch could put on the socket arrives here, and a knob is
        // the least of it: this is also what a swept timebase reads as while it
        // is being swept.
        if (!float.IsFinite(seconds)) return "—";

        return seconds switch
        {
            < 1e-3f => string.Create(CultureInfo.InvariantCulture, $"{seconds * 1e6f:0.#} µs"),
            < 1f => string.Create(CultureInfo.InvariantCulture, $"{seconds * 1e3f:0.#} ms"),
            _ => string.Create(CultureInfo.InvariantCulture, $"{seconds:0.##} s"),
        };
    }

    /// <summary>Whether the editor should let this value rest only on whole numbers.</summary>
    public bool Stepped => Display is PortDisplay.Note;
}
