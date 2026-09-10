namespace Flyback.Core.Graph;

/// <summary>
/// Something that can play the synth from outside the patch: a keyboard on a USB
/// cable, or the one under your hands right now.
/// </summary>
/// <param name="Id">
/// Stable, and what a saved patch stores. A port number would not do — plug the
/// same keyboard into the other socket and the patch would point at nothing.
/// </param>
/// <param name="Name">What the picker shows.</param>
public readonly record struct MidiSource(string Id, string Name);

/// <summary>
/// What there is to play with. Installed by the shell, because a list of
/// instruments is a fact about the room and the engine has never known one.
/// </summary>
/// <remarks>
/// A static, the way <see cref="NodeCatalog.Current"/> is one: what needs the list
/// is <c>MidiExtra.Fields</c>, which hangs off a <see cref="NodeDef"/> built in a
/// static constructor long before there is a window or a device, so there is
/// nowhere to hand it in. Unlike the catalogue it is asked afresh every time,
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
    public static void Install(Func<IReadOnlyList<MidiSource>> sources) => ask = sources;

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

/// <summary>
/// The signals one instrument carries, and how a program names them.
/// </summary>
/// <remarks>
/// These strings are the join between a module compiled at one moment and a
/// hand moving at another — see <see cref="Compile.OpCode.LoadLive"/>. Both ends
/// spell them from here.
/// </remarks>
public static class MidiSignal
{
    /// <summary>The note being held, as a MIDI number — see <see cref="Pitch"/>.</summary>
    public const string Pitch = "pitch";

    /// <summary>One while a key is down, nought while none is.</summary>
    public const string Gate = "gate";

    /// <summary>How hard the note was struck, 0 to 1.</summary>
    public const string Velocity = "velocity";

    /// <summary>
    /// How many notes have been struck since the program started. A count rather
    /// than a pulse, which is what makes a retrigger possible: nothing outside the
    /// program can hand it a signal high for exactly one evaluation, and a number
    /// that only goes up can be differenced inside it by each path at its own rate.
    /// </summary>
    public const string Strikes = "strikes";

    /// <summary>
    /// What one signal of one instrument is called in
    /// <see cref="Compile.CompiledPatch.LiveInputs"/>.
    /// </summary>
    public static string Key(string source, string signal) => Key(source, 1, signal);

    /// <summary>
    /// The signal of one indexed voice. Index one keeps the original key shape so
    /// patches written before indexed MIDI continue to receive their first voice.
    /// </summary>
    public static string Key(string source, int index, string signal) =>
        index == 0 ? $"{source}/auto/{signal}" : index == 1 ? $"{source}/{signal}" : $"{source}/{index}/{signal}";

    public static string AutoKey(string source, Guid node, string signal) =>
        $"{source}/auto/{node:N}/{signal}";
}
