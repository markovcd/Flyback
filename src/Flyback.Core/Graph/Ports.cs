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

    /// <summary>Three signals traveling together as red, green and blue.</summary>
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
/// type id the running catalog does not hold falls back to the knob.
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
/// could catch. Every domain in the catalog is <paramref name="NormalledTo"/>
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
/// <param name="PatchOnly">
/// True when <paramref name="Default"/> is a filler rather than a setting — a
/// value nobody dials, kept only so the compiler has something to read when
/// nothing is wired in. The editor draws no knob for one: a row that moved
/// nothing would be worse than a row that is not there, so it names what the
/// socket does instead — see <c>MainWindow.BuildInputRow</c>. Every
/// <see cref="PortKind.Color"/> input qualifies on its kind alone, per
/// <c>docs/adr/0009-editable-defaults-on-every-input.md</c>: a single float
/// cannot hold a color, so an unwired one is a broadcast gray nothing chose.
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
    bool Swept = false,
    bool PatchOnly = false)
{
    public int Width => Kind == PortKind.Color ? 3 : 1;

    /// <summary>
    /// Whether <see cref="Min"/> and <see cref="Max"/> say what the socket takes or
    /// gives, rather than being the −4..4 a slider gets when nothing was declared.
    /// </summary>
    // ReSharper disable CompareOfFloatsByEqualityOperator
    public bool Ranged => Kind != PortKind.Color && (Min != -4f || Max != 4f);
    // ReSharper restore CompareOfFloatsByEqualityOperator

    /// <summary>
    /// Whether the socket has nothing worth a knob: declared <see cref="PatchOnly"/>,
    /// or a color, which is <see cref="PatchOnly"/> for free — see its doc for why.
    /// </summary>
    public bool NeedsAWire => PatchOnly || Kind == PortKind.Color;

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

    /// <summary>
    /// What this socket is for, in words that stand on their own: the inspector
    /// shows them as the tip on the socket's row, and the assistant reads them
    /// after the socket's name. Every shipped socket has it.
    /// </summary>
    /// <remarks>
    /// Only what is true of this one socket. What the module is for, and how its
    /// sockets work together, is <see cref="NodeDef.Description"/>. A
    /// socket that means the same everywhere takes its words from <see cref="SocketHelp"/>.
    /// </remarks>
    public string Help
    {
        get => field ?? string.Empty;
        init;
    }

    /// <summary>
    /// How far above the bottom of the range a control stops sweeping evenly and
    /// starts sweeping in decades, or 0 for an even sweep throughout. Only the
    /// editor reads it; the stored value is the value.
    /// </summary>
    /// <remarks>
    /// A frequency from a slow wobble to the top of hearing is six decades, and
    /// swept evenly every LFO setting is inside the first thousandth of the travel.
    /// </remarks>
    public float Knee { get; init; }

    /// <summary>
    /// Whether a value past either end of the range still means something here: a
    /// phase or a hue wraps round, and a gate reads a threshold. Such a socket is
    /// not warned about when a wire into it swings past its range.
    /// </summary>
    public bool Lenient { get; init; }

    /// <summary>
    /// Whether this socket means what its name means everywhere, and so takes its
    /// <see cref="Help"/> from <see cref="SocketHelp"/> rather than saying it here.
    /// </summary>
    /// <remarks>
    /// Filled in by <see cref="NodeDef"/>, which knows an input from an output. A
    /// name with no standard is an error in the declaration: a plugin's module that
    /// asks for one is refused, and the built-in catalog does not start.
    /// </remarks>
    public bool Standard { get; init; }

    /// <summary>How far along a control spanning <paramref name="min"/> to <paramref name="max"/> <paramref name="value"/> sits, 0 to 1.</summary>
    public double Travel(float value, float min, float max) => Taper.Travel(value, min, max, Knee);

    /// <summary>The value <paramref name="travel"/> of the way along a control spanning <paramref name="min"/> to <paramref name="max"/>.</summary>
    public float At(double travel, float min, float max) => Taper.At(travel, min, max, Knee);
}
