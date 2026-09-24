using Flyback.Core.Compile;

namespace Flyback.Core.Graph;

public partial class NodeCatalog
{
    private static IEnumerable<NodeDef> Maths()
    {
        var primitives = Primitives().ToList();

        foreach (var def in primitives) yield return def;

        yield return Mixer();

        yield return Desk();

        yield return Expression(Formula.Functions(primitives));
    }

    /// <summary>
    /// The Maths modules that are one op or a handful — everything an Expression
    /// may call.
    /// </summary>
    private static IEnumerable<NodeDef> Primitives()
    {
        yield return Binary("math.add", "Add", OpCode.Add, 0f, "a + b");
        yield return Binary("math.sub", "Subtract", OpCode.Sub, 0f, "a - b");
        yield return Binary("math.mul", "Multiply", OpCode.Mul, 1f, "a * b");
        yield return Binary("math.div", "Divide", OpCode.Div, 1f, "a / b, and 0 when b is 0.");
        yield return Binary("math.mod", "Modulo", OpCode.Mod, 1f, "Remainder of a / b. Wraps values into a band.");
        yield return Binary("math.pow", "Power", OpCode.Pow, 2f, "a raised to b.");
        yield return Binary("math.min", "Minimum", OpCode.Min, 0f, "Whichever of a and b is smaller.");
        yield return Binary("math.max", "Maximum", OpCode.Max, 0f, "Whichever of a and b is larger.");
        yield return Binary("math.atan2", "Atan2", OpCode.Atan2, 1f, "Angle of the vector (b, a).");
        yield return Binary("math.hypot", "Length", OpCode.Hypot, 0f, "Distance from the origin to (a, b).");

        yield return Unary("math.abs", "Absolute", OpCode.Abs, "Drops the sign. Mirrors a signal about zero.");
        yield return Unary("math.neg", "Negate", OpCode.Neg, "Flips the sign.");
        yield return Unary("math.sin", "Sin", OpCode.Sin, "Sine.", Radians);
        yield return Unary("math.cos", "Cos", OpCode.Cos, "Cosine.", Radians);
        yield return Unary("math.tan", "Tan", OpCode.Tan, "Tangent.", Radians);
        yield return Unary("math.sqrt", "Square root", OpCode.Sqrt, "Square root, and 0 for negatives.");
        yield return Unary("math.floor", "Floor", OpCode.Floor, "Rounds down. Quantizes a smooth signal into steps.");
        yield return Unary("math.fract", "Fraction", OpCode.Fract, "Just the part after the decimal point. Wraps to 0..1.");
        yield return Unary("math.sign", "Sign", OpCode.Sign, "-1, 0 or 1.");
        yield return Unary("math.exp", "Exp", OpCode.Exp, "e raised to the input.");
        yield return Unary("math.log", "Log", OpCode.Log, "Natural log, and 0 for non-positive input.");

        yield return new NodeDef(
            "math.clamp", "Clamp", ModuleCategories.Maths,
            [
                Any("in"),
                Any("low", -1f) with { Help = "The bottom of the range." },
                Any("high", 1f) with { Help = "The top of the range." },
            ],
            [Any("out")],
            (em, i) => [em.Ternary(OpCode.Clamp, i[0], i[1], i[2])],
            "Holds the signal inside a range.");

        yield return new NodeDef(
            "math.mix", "Mix", ModuleCategories.Maths,
            [Any("a"), Any("b", 1f), Any("t", 0.5f, 0f, 1f)],
            [Any("out")],
            (em, i) => [em.Ternary(OpCode.Mix, i[0], i[1], i[2])],
            "Blends from 'a' to 'b'.");

        yield return new NodeDef(
            "math.smoothstep", "Smoothstep", ModuleCategories.Maths,
            [
                Any("edge0") with { Help = "Where the ramp starts: 0 below it." },
                Any("edge1", 1f) with { Help = "Where the ramp ends: 1 above it." },
                Any("in"),
            ],
            [Any("out")],
            (em, i) => [em.Ternary(OpCode.Smoothstep, i[0], i[1], i[2])],
            "A soft 0-to-1 ramp between the two edges. The anti-aliased threshold.");

        yield return new NodeDef(
            "math.step", "Threshold", ModuleCategories.Maths,
            [Any("edge") with { Help = "Where it flips: 0 below it, 1 above it." }, Any("in")],
            [Any("out")],
            (em, i) => [em.Binary(OpCode.Step, i[0], i[1])],
            "A hard threshold on 'in'.");

        yield return new NodeDef(
            "math.remap", "Remap", ModuleCategories.Maths,
            [
                Any("in"),
                Num("in low", -1f) with { Help = "The value of 'in' that comes out as 'out low'." },
                Num("in high", 1f) with { Help = "The value of 'in' that comes out as 'out high'." },
                Num("out low") with { Help = "Where 'in low' lands." },
                Num("out high", 1f) with { Help = "Where 'in high' lands." },
            ],
            [Any("out")],
            (em, i) =>
            {
                var t = em.Binary(OpCode.Div, em.Binary(OpCode.Sub, i[0], i[1]), em.Binary(OpCode.Sub, i[2], i[1]));
                return [em.Ternary(OpCode.Mix, i[3], i[4], t)];
            },
            "Rescales one range onto another. Bipolar -1..1 into 0..1 is the common one.");

        yield return new NodeDef(
            AutoRemapTypeId, "Auto remap", ModuleCategories.Maths,
            [
                Any("in"),
                Num("in low", 0f, 0f, 1f) with { Help = "Where it starts, as a fraction of the range of what feeds 'in'." },
                Num("in high", 1f, 0f, 1f) with { Help = "Where it ends, as a fraction of the range of what feeds 'in'." },
                Num("out low", 0f, 0f, 1f) with { Help = "Where 'in low' lands, as a fraction of the range of the socket 'out' feeds." },
                Num("out high", 1f, 0f, 1f) with { Help = "Where 'in high' lands, as a fraction of the range of the socket 'out' feeds." },
            ],
            [Any("out")],
            EmitAutoRemap,
            "A Remap that reads its ranges off its wires. Each knob is 0 to 1 of the range at "
            + "its wire's far end, swept the way that socket's own knob sweeps. Where a wire's far "
            + "end has no range, that pair is plain numbers, as on Remap.");
    }

    public const string AutoRemapTypeId = "math.autoremap";

    /// <summary>
    /// Remap's arithmetic in travel rather than in value: the input is first read as
    /// how far along its source's range it is, and the result is turned into the
    /// destination's range the way that socket's own knob would turn it, so a sweep
    /// into a cutoff moves in octaves all the way along rather than only at its ends.
    /// </summary>
    private static Slot[] EmitAutoRemap(Emitter em, EmitContext node)
    {
        var spans = node.Spans ?? RemapSpans.Unwired;

        // A color bound for a single number is its brightness first, so a sweep
        // follows how light it is rather than an average of three sweeps.
        var input = spans.Narrow ? em.Coerce(node[AutoRemap.In], 1) : node[AutoRemap.In];
        var travel = spans.In is { } from ? Travel(em, input, from) : input;
        var low = node[AutoRemap.InLow];

        var t = em.Binary(OpCode.Div, em.Binary(OpCode.Sub, travel, low), em.Binary(OpCode.Sub, node[AutoRemap.InHigh], low));
        var along = em.Ternary(OpCode.Mix, node[AutoRemap.OutLow], node[AutoRemap.OutHigh], t);

        return [spans.Out is { } into ? At(em, along, into) : along];
    }

    /// <summary><see cref="RemapSpan.Travel"/> as ops.</summary>
    private static Slot Travel(Emitter em, Slot value, RemapSpan span)
    {
        if (span.Knee <= 0f) return em.Mul(em.Add(value, -span.Min), 1f / (span.Max - span.Min));

        var low = MathF.Min(span.Min, span.Max);
        var decades = em.Unary(OpCode.Log, em.Add(em.Mul(em.Add(value, -low), 1f / span.Knee), 1f));
        var up = em.Mul(decades, 1f / MathF.Log(1f + MathF.Abs(span.Max - span.Min) / span.Knee));

        return span.Max < span.Min ? em.Add(em.Mul(up, -1f), 1f) : up;
    }

    /// <summary><see cref="RemapSpan.At"/> as ops.</summary>
    private static Slot At(Emitter em, Slot travel, RemapSpan span)
    {
        if (span.Knee <= 0f) return em.Add(em.Mul(travel, span.Max - span.Min), span.Min);

        var up = span.Max < span.Min ? em.Add(em.Mul(travel, -1f), 1f) : travel;
        var rise = em.Unary(OpCode.Exp, em.Mul(up, MathF.Log(1f + MathF.Abs(span.Max - span.Min) / span.Knee)));

        return em.Add(em.Mul(em.Add(rise, -1f), span.Knee), MathF.Min(span.Min, span.Max));
    }

    private static NodeDef Unary(string id, string name, OpCode code, string description, string help = "") => new(
        id, name, ModuleCategories.Maths, [Any("in") with { Help = help }], [Any("out")],
        (em, i) => [em.Unary(code, i[0])], description);

    private const string Radians = "An angle, in radians.";

    private static NodeDef Binary(
        string id, string name, OpCode code, float defaultB, string description) => new(
        id, name, ModuleCategories.Maths, [Any("a"), Any("b", defaultB)], [Any("out")],
        (em, i) => [em.Binary(code, i[0], i[1])], description);
    
    /// <summary>
    /// Four inputs, a level on each, summed into one — the desk, rather than four
    /// Multiplies wired into a chain of Adds.
    /// </summary>
    /// <remarks>
    /// Every socket is an <see cref="PortKind.Any"/>, so this is one module for both
    /// halves of the machine: four tones sum to a chord and four fields to an image. A
    /// level is a socket like any other, which is what makes a fader something an
    /// oscillator can sweep.
    /// </remarks>
    public const string MixerTypeId = "math.mixer";

    private static NodeDef Mixer()
    {
        const int channels = 4;

        var ports = new PortSpec[channels * 2];
        for (var ch = 0; ch < channels; ch++)
        {
            ports[ch * 2] = Any($"in {ch + 1}");
            ports[ch * 2 + 1] = Any($"level {ch + 1}", 1f, 0f, 1f) with { Help = $"Multiplies 'in {ch + 1}' before the sum." };
        }

        return new NodeDef(
            MixerTypeId, "Mixer", ModuleCategories.Routing,
            ports, [Any("out")],
            (em, i) =>
            {
                var sum = em.Mul(i[0], i[1]);
                for (var ch = 1; ch < channels; ch++)
                    sum = em.Add(sum, em.Mul(i[ch * 2], i[ch * 2 + 1]));
                return [sum];
            },
            "Four signals summed, each through its level. It sums rather than averages, so "
            + "four at full is four times as loud. An unused input adds nothing. Colors mix too: "
            + "the levels are a four-way blend of pictures.");
    }

    /// <summary>
    /// The Mixer twice over with the end of the chain built in: four stereo
    /// channels summed to a left and a right, trimmed, and held to the rails.
    /// </summary>
    /// <remarks>
    /// A track's last box is otherwise the same Mixer built once for each ear, a
    /// Multiply under unity on each and a Clamp on each. A 'right' left unpatched
    /// carries its 'left', so a mono voice is one wire. The 'bus' sockets are how
    /// two of these become one desk of eight: the 'bus' outputs are the sum before
    /// the trim and the rails, and the next Desk adds them in at unity — so only the
    /// last in the chain trims and only the last can clip.
    /// </remarks>
    public const string DeskTypeId = "math.desk";

    private static NodeDef Desk()
    {
        const int channels = 4;
        const int busLeft = channels * 3;
        const int busRight = busLeft + 1;
        const int trim = busLeft + 2;

        var ports = new PortSpec[trim + 1];
        for (var ch = 0; ch < channels; ch++)
        {
            ports[ch * 3] = new PortSpec($"left {ch + 1}", PatchOnly: true);
            ports[ch * 3 + 1] = new PortSpec($"right {ch + 1}", NormalledFrom: ch * 3, PatchOnly: true)
            {
                Help = $"Carries 'left {ch + 1}' while unpatched, so a mono voice is one wire.",
            };
            ports[ch * 3 + 2] = Num($"level {ch + 1}", 1f, 0f, 1f) with { Help = $"Multiplies both 'left {ch + 1}' and 'right {ch + 1}'." };
        }

        ports[busLeft] = new PortSpec("bus left", PatchOnly: true) { Help = "Another Desk's 'bus left', added in at full level." };
        ports[busRight] = new PortSpec("bus right", PatchOnly: true) { Help = "Another Desk's 'bus right', added in at full level." };
        ports[trim] = Num("trim", 1f, 0f, 2f) with { Help = "Multiplies the sum before it is held to -1..1." };

        const string master = "The sum times 'trim', held to -1..1: patch it into the Output.";
        const string chain = "The sum before 'trim' and the rails, for the same 'bus' input on the next Desk.";

        return new NodeDef(
            DeskTypeId, "Desk", ModuleCategories.Routing,
            ports,
            [
                Num("left") with { Help = master },
                Num("right") with { Help = master },
                Num("bus left") with { Help = chain },
                Num("bus right") with { Help = chain },
            ],
            (em, i) =>
            {
                var left = Side(0, busLeft);
                var right = Side(1, busRight);

                return [Railed(left), Railed(right), left, right];

                Slot Side(int side, int bus)
                {
                    var sum = em.Mul(i[side], i[2]);
                    for (var ch = 1; ch < channels; ch++)
                        sum = em.Add(sum, em.Mul(i[ch * 3 + side], i[ch * 3 + 2]));
                    return em.Add(sum, i[bus]);
                }

                Slot Railed(Slot sum) =>
                    em.Ternary(OpCode.Clamp, em.Mul(sum, i[trim]), em.Constant(-1f), em.Constant(1f));
            },
            "A stereo mixer for the end of a patch: four channels, each a 'left', a 'right' and "
            + "one 'level'. For more channels chain Desks, 'bus' out into the next one's 'bus' in, "
            + "and the last Desk is the master.");
    }

    /// <summary>
    /// A formula over four sockets, typed rather than wired: one block where the
    /// arithmetic would otherwise be a column of Multiplies and Adds.
    /// </summary>
    /// <remarks>
    /// It is the Maths modules its formula names and nothing else — see
    /// <see cref="Formula"/> — so a formula is heard and seen exactly as the
    /// modules it spells would be. The sockets rest where the Multiply's do, on
    /// nought and one, so the formula a fresh one carries passes 'a' through
    /// until something is wired into it.
    /// </remarks>
    private static NodeDef Expression(IReadOnlyDictionary<string, NodeDef> functions) => new(
        ExpressionTypeId, "Expression", ModuleCategories.Maths,
        [Operand("a"), Operand("b", 1f), Operand("c"), Operand("d")], [Any("out")],
        (em, i) =>
            [i.Extra<Formula>(FormulaExtra.StateKey) is { } formula ? formula.Lower(em, i.Resolve) : em.Constant(0f)],
        "A formula over its four sockets: 'a * b + c', 'smoothstep(0.2, 0.8, a) * b'. "
        + "Exactly the Maths modules it names. It knows + - * / %, a minus in front, "
        + "brackets, numbers, pi and tau, and " + Formula.Listed(functions)
        + "; an argument left off is that module's knob at rest. A number to be turned "
        + "belongs on a socket. A formula that does not read gives 0 and says why.")
    {
        Extras = [new FormulaExtra(functions)],
        AsksForItsInputs = true,
    };

    private static PortSpec Operand(string name, float value = 0f) =>
        Any(name, value) with { Help = $"Read as '{name}' in the formula." };

    public const string ExpressionTypeId = "math.expression";

    /// <summary>An Expression's formula as typed, and null for any other module.</summary>
    public static string? FormulaOf(NodeInstance node) =>
        node.TypeId == ExpressionTypeId ? FormulaExtra.Of(node) : null;

    /// <summary>
    /// What stops <paramref name="formula"/> being read, and where, or null where
    /// nothing does. The reading the module compiles with, so the panel can say
    /// what the compiler is about to.
    /// </summary>
    public static string? FormulaProblem(string formula)
    {
        if (Get(ExpressionTypeId)?.Extra<FormulaExtra>() is not { } extra) return null;

        Formula.Read(formula, extra.Functions, out var problem);
        return problem;
    }
}

/// <summary>An Expression's formula: one line of text, read where the module is compiled.</summary>
/// <remarks>
/// Declared as a <see cref="ExtraField.Text"/> field, so the panel, the text
/// language and the assistant write it the way they write any plugin's field
/// (ADR-0055). What is its own is reading it: a formula that does not read is a
/// complaint here and nought out of the module, the bargain a missing file has
/// with a Sample.
/// </remarks>
internal sealed record FormulaExtra(IReadOnlyDictionary<string, NodeDef> Functions) : NodeExtra
{
    /// <summary>What this is filed under, in a saved patch and on a context.</summary>
    public const string StateKey = "expression";

    /// <summary>The one field: the formula as typed.</summary>
    public const string FormulaField = "formula";

    /// <summary>What a fresh one carries: the Multiply and Add a formula most often replaces.</summary>
    public const string Fresh = "a * b + c";

    public override string Key => StateKey;

    public override IReadOnlyList<ExtraField> Fields { get; } = [new ExtraField.Text(FormulaField, "formula", Fresh)];

    public override EmitContext Fold(EmitContext ctx, NodeInstance node, ExtraEnv env)
    {
        var text = Of(node);

        if (Formula.Read(text, Functions, out var problem) is { } formula) return ctx.With(Key, formula);

        env.Report(new CompileIssue(
            node.Id,
            $"'{env.Title}' does not read as a formula: {problem}. It gives 0 until it does."));

        return ctx;
    }

    /// <summary>The formula an instance carries, as typed.</summary>
    public static string Of(NodeInstance node) =>
        ((ExtraField.Text)FieldOf).Value(node.StateOf(StateKey)?[FormulaField]);

    public override string Announce() =>
        $"  {StateKey} {FormulaField}, the formula as a string — not a knob";

    private static readonly ExtraField FieldOf = new ExtraField.Text(FormulaField, "formula", Fresh);
}
