using System.Globalization;

namespace Flyback.Core.Graph;

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
/// Index of an earlier input this one falls back to when unpatched, or -1 for
/// <paramref name="Default"/>, like a normalled right-channel jack.
/// </param>
/// <param name="Display">How the editor should write the value out.</param>
/// <param name="NormalledTo">
/// A hidden module driving this input while unpatched, or null to rest on
/// <paramref name="Default"/>. Such a socket has no knob; a wire overrides it.
/// </param>
/// <param name="Domain">
/// True when this input is the axis the module is read across rather than a value
/// it uses, so a constant on it is always a mistake.
/// </param>
/// <param name="Swept">
/// True when the module reads this input over a domain of its own making; the
/// compiler leaves it for the module to resolve with <see cref="EmitContext.Resolve"/>.
/// </param>
/// <param name="PatchOnly">
/// True when <paramref name="Default"/> is only a filler for the compiler, so
/// the editor draws no knob for it. Every <see cref="PortKind.Color"/> input
/// qualifies (ADR-0009).
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
    /// <summary>How many registers the socket's value takes: 3 for a color, 1 otherwise.</summary>
    internal int Width => Kind == PortKind.Color ? 3 : 1;

    /// <summary>
    /// Whether <see cref="Min"/> and <see cref="Max"/> say what the socket takes or
    /// gives, rather than being the −4..4 a slider gets when nothing was declared.
    /// </summary>
    // ReSharper disable CompareOfFloatsByEqualityOperator
    internal bool Ranged => Kind != PortKind.Color && (Min != -4f || Max != 4f);
    // ReSharper restore CompareOfFloatsByEqualityOperator

    /// <summary>
    /// Whether the socket has nothing worth a knob: declared <see cref="PatchOnly"/>,
    /// or a color, which is <see cref="PatchOnly"/> for free — see its doc for why.
    /// </summary>
    internal bool NeedsAWire => PatchOnly || Kind == PortKind.Color;

    /// <summary>
    /// The value as it should be shown for this socket. One place, because the
    /// node on the canvas and the row in the inspector have to agree. The case
    /// matches how the module reads the number:
    /// <see cref="PortDisplay.Note"/> rounds because Note rounds.
    /// </summary>
    internal string Format(float value) => Display switch
    {
        PortDisplay.Note => Pitch.Name(value),
        PortDisplay.Duration => Time(value),
        PortDisplay.Integer => value.ToString("0", CultureInfo.InvariantCulture),
        PortDisplay.Chord => Chords.Label(value),
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
    internal bool Stepped => Display is PortDisplay.Note or PortDisplay.Integer or PortDisplay.Chord;

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
    internal double Travel(float value, float min, float max) => Taper.Travel(value, min, max, Knee);

    /// <summary>The value <paramref name="travel"/> of the way along a control spanning <paramref name="min"/> to <paramref name="max"/>.</summary>
    internal float At(double travel, float min, float max) => Taper.At(travel, min, max, Knee);
}