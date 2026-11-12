namespace Flyback.Core.Graph;

/// <summary>The shorthands the built-in modules declare their sockets with.</summary>
internal static class Sockets
{
    /// <summary>A number socket with its knob's default and range.</summary>
    internal static PortSpec Num(string name, float value = 0f, float min = -4f, float max = 4f) =>
        new(name, PortKind.Scalar, value, min, max);

    /// <summary>A color socket.</summary>
    internal static PortSpec Col(string name) => new(name, PortKind.Color);

    /// <summary>A socket that takes a number or a color.</summary>
    internal static PortSpec Any(string name, float value = 0f, float min = -4f, float max = 4f) =>
        new(name, PortKind.Any, value, min, max);

    /// <summary>The hidden clock every domain socket is normalled to.</summary>
    internal static PortNormal Clock => new(SourceModules.TimeTypeId);

    /// <summary>The hidden x and y every socket that wants a position is normalled to.</summary>
    internal static PortNormal Across => new(SourceModules.CoordTypeId);

    /// <summary>The hidden y every socket that wants a position down the screen is normalled to.</summary>
    internal static PortNormal Down => new(SourceModules.CoordTypeId, SourceModules.CoordYPort);

    /// <summary>
    /// An oscillator's rate, from standing still to the top of hearing: the lower
    /// half of the knob's travel is LFO territory up to 20 Hz, the upper half is pitch.
    /// </summary>
    internal static PortSpec Freq => new("freq", PortKind.Scalar, 1f, 0f, 20_000f) { Knee = 0.02f, Standard = true };

    /// <summary>
    /// The axis a module is read across rather than a value it uses. Named at the
    /// port because only the module knows which input that is.
    /// </summary>
    /// <remarks>
    /// Normalled to Time, because a domain resting on a knob is a module that does
    /// not move — see <see cref="PortSpec.NormalledTo"/>. Every domain in the
    /// catalog is built through here, so this one line is the whole of "an
    /// oscillator runs unless you say otherwise".
    /// </remarks>
    internal static PortSpec Domain(string name, string help = "") =>
        new(name, NormalledTo: Clock, Domain: true) { Help = help, Standard = help.Length == 0 };

    /// <summary>
    /// Where on the screen a module is being asked about, normalled to Coordinates
    /// so the pixel's own position is what it reads until a patch says different.
    /// Declared as a pair because it is always a pair: given one and left holding
    /// a knob on the other, a module reads along a line through the picture rather
    /// than across it.
    /// </summary>
    internal static PortSpec[] Position() =>
    [
        new("x", NormalledTo: Across) { Standard = true },
        new("y", NormalledTo: Down) { Standard = true },
    ];

    /// <summary>An input that carries an earlier one through when left unpatched.</summary>
    internal static PortSpec Normalled(string name, int from, float min = -4f, float max = 4f) =>
        new(name, PortKind.Scalar, 0f, min, max, from);

    /// <summary>
    /// An input the module reads over a domain of its own rather than over the
    /// pixel's — see <see cref="PortSpec.Swept"/>. Untyped, so a color may be
    /// looked at as readily as a scalar.
    /// </summary>
    internal static PortSpec Swept(string name) =>
        new(name, PortKind.Any, Swept: true);

    /// <summary>A note number, which the editor writes out by name rather than as a number.</summary>
    internal static PortSpec Pitched(string name, float value) =>
        new(name, PortKind.Scalar, value, 0f, 127f, -1, PortDisplay.Note);

    /// <summary>
    /// A length of time, held in decades of seconds and written out as the time
    /// it is — see <see cref="PortDisplay.Duration"/>. A hundred microseconds to
    /// half a minute: one audio cycle at the bottom, a slow LFO at the top.
    /// </summary>
    internal static PortSpec Seconds(string name, float value) =>
        new(name, PortKind.Scalar, value, -4f, 1.5f, -1, PortDisplay.Duration);
}
