namespace Flyback.Core.Compile;

/// <summary>
/// The names a Meter's readings are played on, which is all the module and the
/// thing measuring for it have to agree about.
/// </summary>
/// <remarks>
/// Apart from whatever does the measuring, because the two live on different
/// sides: a Meter is a module and asks for a name while it is lowered, and the
/// measurement is made outside both programs from the rings the speakers fill.
/// </remarks>
public static class MeterSignals
{
    /// <summary>
    /// What a Meter listens on, before the signal it wants. The prefix is a word
    /// no instrument can take, so a meter and a keyboard can never collide in one
    /// block — see <c>LiveValues</c>, which is keyed by name for this
    /// reason.
    /// </summary>
    private const string Prefix = "meter";

    /// <summary>The loudness of the window, which is what a level meter shows.</summary>
    public const string Level = "level";

    /// <summary>The furthest the window got from nought, which is what hits.</summary>
    public const string Peak = "peak";

    /// <summary>
    /// The name one Meter's reading is played on. Built from the node id because
    /// the two programs of a patch share no numbering and this is the only thing
    /// they do share.
    /// </summary>
    public static string Key(Guid node, string signal) => $"{Prefix}/{node:N}/{signal}";
}
