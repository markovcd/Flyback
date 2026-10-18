using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;

namespace Flyback.Plugins.Assist;

/// <summary>
/// How the text writes what a module carries that is not a knob, for a module listing to say.
/// </summary>
internal static class Vocabulary
{
    /// <summary>Where <paramref name="extra"/> goes in a module's call.</summary>
    /// <remarks>
    /// A file, whoever's kind it is, is the call's one unnamed string; anything else not
    /// named here is a field a plugin declared through <see cref="NodeExtra.Fields"/>
    /// (ADR-0055), which is a named argument.
    /// </remarks>
    public static string WrittenAs(NodeExtra extra) => extra switch
    {
        StepsExtra => "a block after the call, in step notation",
        ScaleExtra => "a block of note names after the call",
        ArrangementExtra => "a block of parts after the call",
        FileExtra => "the call's one unnamed string",
        _ => "named arguments",
    };

    /// <summary>
    /// One extra's line in a module listing: what the module carries, which the
    /// engine says, and how the text writes it, which this project says.
    /// </summary>
    public static string Announce(NodeExtra extra) =>
        $"{extra.Announce()}, written as {WrittenAs(extra)}";
}
