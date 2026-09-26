namespace Flyback.Core.Graph;

/// <summary>
/// What there is to play with. Installed by the shell, because a list of
/// instruments is a fact about the room and the engine has never known one.
/// </summary>
/// <remarks>
/// A static, the way <see cref="NodeCatalog.Current"/> is one: what needs the list
/// is <c>MidiExtra.Fields</c>, which hangs off a <see cref="NodeDef"/> built in a
/// static constructor long before there is a window or a device, so there is
/// nowhere to hand it in. Unlike the catalog it is asked afresh every time,
/// because devices are plugged in and pulled out while the program runs; what is
/// frozen is the choice a patch stores, which is a string.
/// </remarks>
public static class MidiSources
{
    /// <summary>
    /// The computer's own keyboard, which is always there and needs no driver.
    /// Named here because the module defaults to it and the shell answers for
    /// it, and a string spelled in two places is a string that will differ in
    /// one of them.
    /// </summary>
    public const string Keyboard = "keyboard";

    /// <summary>What the shell offers, and the keyboard alone until it has said.</summary>
    private static Func<IReadOnlyList<MidiSource>> ask = Alone;

    /// <summary>
    /// Points the module at whatever is actually plugged in. Called once during
    /// startup; what it hands back may differ on every call after.
    /// </summary>
    internal static void Install(Func<IReadOnlyList<MidiSource>> sources) => ask = sources;

    /// <summary>
    /// Everything that could play a patch right now. Total, whatever the shell does:
    /// a backend enumerating hardware is opening something that may be busy or gone,
    /// so a source list that throws is read as an empty one and the keyboard is put
    /// back in front of it.
    /// </summary>
    public static IReadOnlyList<MidiSource> All
    {
        get
        {
            IReadOnlyList<MidiSource> offered;

            try
            {
                offered = ask();
            }
            catch
            {
                offered = [];
            }

            return offered.Any(s => s.Id == Keyboard) ? offered : [.. Alone(), .. offered];
        }
    }

    private static IReadOnlyList<MidiSource> Alone() =>
        [new MidiSource(Keyboard, "Computer keyboard")];
}