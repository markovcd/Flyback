using System.Globalization;

namespace Flyback.Core.Graph;

/// <summary>
/// How an input's value should be read back, and whether the editor lets the
/// control rest between whole numbers — see <see cref="PortSpec.Stepped"/>. The
/// compiler never consults it: nothing here changes what a stored number means,
/// and a signal arriving down a wire is untouched by any of it.
/// </summary>
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
    /// No slider spans the five decades from a fraction of an audio cycle to half
    /// a minute: at a maximum of thirty seconds every audio-rate setting is inside
    /// the first thousandth of the travel. In decades the range is one even sweep,
    /// which is why the control on a scope is marked this way.
    /// </remarks>
    Duration,

    /// <summary>A whole-number setting.</summary>
    Integer,
}

/// <summary>What flows down a wire.</summary>
public enum PortKind
{
    /// <summary>A single value that varies over x, y and t — the audio-rate signal of a video synth.</summary>
    Scalar,

    /// <summary>Three signals travelling together as red, green and blue.</summary>
    Color,

    /// <summary>
    /// Whatever is plugged in, passed through unchanged. Maths modules use this
    /// so a single Multiply works on both a scalar and a color, the way a
    /// shading language overloads its operators.
    /// </summary>
    Any,
}

/// <summary>
/// A module the compiler patches into a socket that nothing else is patched into
/// — the rack's normalled bus, where an unplugged jack already carries the signal
/// that socket is nearly always used with.
/// </summary>
/// <remarks>
/// Named by type id rather than held as a definition, so a plugin can normal one
/// of its sockets to <c>time</c> without the engine knowing that plugin exists. A
/// type id the running catalogue does not hold falls back to the knob.
/// <para>
/// What is patched in is one hidden instance shared by every socket normalled to
/// it, carrying no knobs of its own: there is no node on the canvas for anybody
/// to have turned one on.
/// </para>
/// </remarks>
/// <param name="TypeId">The module to read, as a saved patch would name it.</param>
/// <param name="Port">Which of that module's outputs.</param>
public readonly record struct PortNormal(string TypeId, int Port = 0);

/// <summary>
/// One input or output socket on a node. Inputs carry a <see cref="Default"/>
/// that is editable on the node itself, so most patches need no constant nodes.
/// </summary>
/// <param name="Name">Label shown next to the socket.</param>
/// <param name="Kind">Scalar or color.</param>
/// <param name="Default">Value used when nothing is plugged in.</param>
/// <param name="Min">Lower end of the slider range in the editor.</param>
/// <param name="Max">Upper end of the slider range in the editor.</param>
/// <param name="NormalledFrom">
/// Index of an earlier input this one falls back to when nothing is patched in,
/// or -1 to fall back to <paramref name="Default"/> — the hardware normalled
/// jack, where leaving the right channel unpatched carries the left through.
/// </param>
/// <param name="Display">How the editor should write the value out.</param>
/// <param name="NormalledTo">
/// A hidden module driving this input while nothing is patched into it, or null
/// to rest on <paramref name="Default"/>. A wire overrides it exactly as a wire
/// overrides a knob, and unplugging brings it back. Such a socket has no knob to
/// turn while the normal holds; a patch that wants a constant there adds a Value
/// module.
/// </param>
/// <param name="Domain">
/// True when this input is the axis the module is read across rather than a value
/// it uses. A constant is never sensible on one: an oscillator that does not move
/// produces a fixed value and a sequencer sits on one step, both of which compile
/// perfectly, which is what made a domain left alone the one mistake nothing
/// could catch. Every domain in the catalogue is <paramref name="NormalledTo"/>
/// Time; the complaint survives for one normalled to nothing.
/// </param>
/// <param name="Swept">
/// True when the module reads this input over a domain of its own making rather
/// than over the pixel's. The compiler leaves it unresolved and hands the module
/// a way to resolve it itself, so whatever it does to the domain first is in
/// force before everything upstream is lowered — see
/// <see cref="EmitContext.Resolve"/>. The opposite of
/// <paramref name="Domain"/>: read under a domain the module supplies rather than
/// across one the port names.
/// </param>
public readonly record struct PortSpec(
    string Name,
    PortKind Kind = PortKind.Scalar,
    float Default = 0f,
    float Min = -4f,
    float Max = 4f,
    int NormalledFrom = -1,
    PortDisplay Display = PortDisplay.Number,
    PortNormal? NormalledTo = null,
    bool Domain = false,
    bool Swept = false)
{
    public int Width => Kind == PortKind.Color ? 3 : 1;

    /// <summary>
    /// The value as it should be shown for this socket. One place, because the
    /// node on the canvas and the row in the inspector have to agree. The case
    /// matches how the module reads the number:
    /// <see cref="PortDisplay.Note"/> rounds because Note rounds.
    /// </summary>
    public string Format(float value) => Display switch
    {
        PortDisplay.Note => Pitch.Name(value),
        PortDisplay.Duration => Time(value),
        PortDisplay.Integer => value.ToString("0", CultureInfo.InvariantCulture),
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
    public bool Stepped => Display is PortDisplay.Note or PortDisplay.Integer;
}
