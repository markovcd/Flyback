using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Picture;

/// <summary>
/// One color laid over another with a blend mode, through a mask.
/// </summary>
/// <remarks>
/// The mode is carried on the node rather than taken from a socket, so only the
/// chosen blend is emitted: the compiler drops unused modules, not unused outputs.
/// </remarks>
internal static class LayerModule
{
    public const string TypeId = "flyback.picture.layer";

    public static NodeDef Definition { get; } = new(
        TypeId, "Layer", ModuleCategories.Color,
        [
            new PortSpec("base", PortKind.Color),
            new PortSpec("top", PortKind.Color),
            new PortSpec("amount", PortKind.Scalar, 1f, 0f, 1f)
            {
                Help = "How much of 'top' shows. Patch a Fill into it to show the layer only inside a shape.",
            },
        ],
        [new PortSpec("color", PortKind.Color)],
        Emit,
        "Lays 'top' over 'base' like an image editor's layer. The mode is set on the node: "
        + "normal, add, screen, multiply, overlay, difference, lighten or darken.")
    {
        Extras = [new ModeExtra()],
    };

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var mode = ModeExtra.Of(node.Extra<ExtraState>(ModeExtra.StateKey));

        var one = em.Constant(1f);
        var a = node[0];
        var b = node[1];

        var blended = mode switch
        {
            Mode.Add => em.Add(a, b),
            Mode.Multiply => em.Mul(a, b),
            Mode.Screen => Screen(a, b),
            // Multiply below mid-gray on the base, screen above it, both doubled so they meet at 0.5.
            Mode.Overlay => em.Ternary(
                OpCode.Mix,
                em.Mul(em.Mul(a, b), 2f),
                em.Sub(one, em.Mul(em.Mul(em.Sub(one, a), em.Sub(one, b)), 2f)),
                em.Binary(OpCode.Step, em.Constant(0.5f), a)),
            Mode.Difference => em.Unary(OpCode.Abs, em.Sub(a, b)),
            Mode.Lighten => em.Binary(OpCode.Max, a, b),
            Mode.Darken => em.Binary(OpCode.Min, a, b),
            _ => b,
        };

        var amount = em.Ternary(OpCode.Clamp, node[2], em.Constant(0f), one);

        return [em.Ternary(OpCode.Mix, a, blended, amount)];

        Slot Screen(Slot x, Slot y) => em.Sub(one, em.Mul(em.Sub(one, x), em.Sub(one, y)));
    }

    private enum Mode { Normal, Add, Multiply, Screen, Overlay, Difference, Lighten, Darken }

    private sealed record ModeExtra : NodeExtra
    {
        public const string StateKey = "layer";
        public const string ModeKey = "mode";

        public override string Key => StateKey;

        public override IReadOnlyList<ExtraField> Fields =>
        [
            new ExtraField.Choice(ModeKey, "mode", Options, Id(Mode.Normal)),
        ];

        private static IReadOnlyList<ChoiceOption> Options { get; } =
            [.. Enum.GetValues<Mode>().Select(mode => new ChoiceOption(Id(mode), mode.ToString()))];

        public override string Report(NodeInstance node) =>
            $"Blends in {Id(Of(new ExtraState(Fields, node.StateOf(StateKey))))} mode.";

        public override string Announce() =>
            $"  {StateKey,-6} mode, one of {string.Join(", ", Options.Select(o => o.Id))}, as a string; not a knob";

        /// <summary>The stored mode, or normal for one this build does not know.</summary>
        public static Mode Of(ExtraState? state) =>
            state is not null && Enum.TryParse<Mode>(state.Chosen(ModeKey), ignoreCase: true, out var mode)
                ? mode
                : Mode.Normal;

        private static string Id(Mode mode) => mode.ToString().ToLowerInvariant();
    }
}
