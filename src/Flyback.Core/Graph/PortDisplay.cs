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

    /// <summary>A Chord's pick, shown as the chord's name: 4 reads as "maj".</summary>
    Chord,
}