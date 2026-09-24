using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Plugins;

namespace Flyback.Plugins.Effects;

/// <summary>
/// Two echoes in time with the music, one to each side: two Delays whose times are
/// counted in steps of the beat rather than typed in seconds.
/// </summary>
/// <remarks>
/// The arithmetic of what it replaces: the tempo times the steps in a beat, a Divide
/// for each side with the count of steps on top, and one of the engine's own Delays
/// for each. How the two are fed is a setting because it changes what is wired to
/// what. In a row the right tap hears the left one, so the repeats cross from side
/// to side, and only the left feeds back — the right repeats what reaches it
/// already. Side by side both hear the input and both feed back.
/// </remarks>
internal static class EchoModule
{
    public const string TypeId = "flyback.effects.echo";

    public const string StateKey = "echo";

    public const string TapsKey = "taps";

    public const string DivisionKey = "division";

    /// <summary>The taps setting that is not the default: both taps hear the input.</summary>
    public const string SideBySide = "side";

    private const string InARow = "row";

    private static readonly ExtraField.Number Division = new(
        DivisionKey,
        "steps per beat",
        new PortSpec("steps per beat", PortKind.Scalar, 4f, 1f, 16f, -1, PortDisplay.Integer));

    public static NodeDef Definition { get; } = new(
        TypeId, "Echo", ModuleCategories.TimeEffects,
        [
            new PortSpec("in", PortKind.Scalar, 0f, -1f, 1f),
            new PortSpec("tempo", PortKind.Scalar, 2f, 0.25f, 8f) { Help = "Beats per second: patch a Tempo here." },
            new PortSpec("left", PortKind.Scalar, 3f, 0.25f, 16f) { Help = "The left tap's time, in steps." },
            new PortSpec("right", PortKind.Scalar, 2f, 0.25f, 16f)
            {
                Help = "The right tap's time, in steps. In a row it repeats the left tap this much later.",
            },
            new PortSpec("feedback", PortKind.Scalar, 0.45f, 0f, 0.95f)
            {
                Help = "How much comes round for the next repeat. In a row only the left tap feeds back.",
            },
            new PortSpec("mix", PortKind.Scalar, 0.4f, 0f, 1f) { Help = "Dry against wet: 1 is a send." },
        ],
        [new PortSpec("left"), new PortSpec("right")],
        Emit,
        "A stereo echo that keeps time: with a Tempo in 'tempo', 'left' and 'right' are counted "
        + "in steps, so 3 and 2 at four steps a beat are a dotted eighth and the beat after. Set "
        + "on the node: steps per beat, and whether the taps are in a row (the repeats cross "
        + "over) or side by side. Two seconds at most. Audio only.")
    {
        Extras =
        [
            new SettingsExtra(
                StateKey,
                [
                    new ExtraField.Choice(
                        TapsKey,
                        "taps",
                        [new ChoiceOption(InARow, "In a row"), new ChoiceOption(SideBySide, "Side by side")],
                        InARow),
                    Division,
                ]),
        ],
        Skin = new ModuleSkin.Palette(CategoryAccents.Of(ModuleCategories.TimeEffects))
        {
            Glyph = "M6,20 A9,9 0 0 1 6,4 M9,20 A6,6 0 0 1 9,8 M12,20 A3,3 0 0 1 12,14",
        },
    };

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var settings = node.Extra<ExtraState>(StateKey);
        var apart = settings?.Chosen(TapsKey) == SideBySide;

        var steps = em.Mul(node[1], settings?.Number(DivisionKey) ?? Division.Spec.Default);
        var leftTime = em.Binary(OpCode.Div, node[2], steps);
        var rightTime = em.Binary(OpCode.Div, node[3], steps);

        var left = NodeCatalog.DelayEchoed(em, node[0], leftTime, node[4], node[5]);

        var right = apart
            ? NodeCatalog.DelayEchoed(em, node[0], rightTime, node[4], node[5])
            : NodeCatalog.DelayEchoed(em, left, rightTime, em.Constant(0f), node[5]);

        return [left, right];
    }
}
