using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Figures;

/// <summary>
/// A struck rectangular plate: the ring of its modes to the ear, and the sand
/// figure they settle into to the eye, from one strike.
/// </summary>
/// <remarks>
/// Each mode (m, n) is a decaying cosine at its own pitch, excited by how much
/// the strike point moves it, which is the plate's own shape at that point. The
/// sound is the sum. Sand is thrown wherever the plate's acceleration passes a
/// threshold, and acceleration goes as the square of a mode's frequency, so a
/// strike clears the plate, the figure comes out as the ring fades, high modes'
/// lines first and the lowest mode's last, and a plate left alone whitens over.
/// The sound is bright at the strike and pure by the end, being the same
/// envelopes.
/// <para>
/// A plate's mode pitches go as the squares of the mode numbers, where a
/// membrane's go as the root of that; the squares are what make it metal. The
/// mode count is set on the node, since it decides how long the program is
/// (ADR-0051).
/// </para>
/// </remarks>
internal static class PlateModule
{
    public const string TypeId = "flyback.figures.plate";

    public const int TriggerPort = 0;
    public const int VelocityPort = 1;
    public const int FreqPort = 2;
    public const int AspectPort = 3;
    public const int DecayPort = 4;
    public const int BrightnessPort = 5;
    public const int StrikeXPort = 6;
    public const int StrikeYPort = 7;

    public const int OutPort = 0;
    public const int FigurePort = 1;
    public const int MotionPort = 2;

    private const float Tau = 6.283185307179586f;
    private const float Pi = 3.1415926535f;

    /// <summary>
    /// The acceleration under which sand stays and over which it is thrown off, in
    /// units where the lowest mode struck as hard as it can be shakes its middle by
    /// one. A high mode shakes far harder than its size, so it clears the plate
    /// at a strike and lets go first.
    /// </summary>
    private const float Rests = 0.15f, Thrown = 0.5f;

    public static NodeDef Definition { get; } = new(
        TypeId, "Plate", FiguresPlugin.Category,
        [
            new PortSpec("trigger", PortKind.Scalar, 0f, 0f, 1f) { Lenient = true },
            new PortSpec("velocity", PortKind.Scalar, 1f, 0f, 1f),
            new PortSpec("freq", PortKind.Scalar, 110f, 20f, 4000f) { Knee = 20f },
            new PortSpec("aspect", PortKind.Scalar, 1.3f, 1f, 2f),
            new PortSpec("decay", PortKind.Scalar, 3f, 0.1f, 8f),
            new PortSpec("brightness", PortKind.Scalar, 0.5f, 0f, 1f),
            new PortSpec("strike x", PortKind.Scalar, 0.3f, 0f, 1f),
            new PortSpec("strike y", PortKind.Scalar, 0.4f, 0f, 1f),
            new PortSpec("x", NormalledTo: NodeCatalog.Across),
            new PortSpec("y", NormalledTo: NodeCatalog.Down),
        ],
        [
            new PortSpec("out", PortKind.Scalar, 0f, -1f, 1f),
            new PortSpec("figure", PortKind.Scalar, 0f, 0f, 1f),
            new PortSpec("motion", PortKind.Scalar, 0f, 0f, 1f),
        ],
        Emit,
        "A plate struck on 'trigger', as hard as 'velocity'. 'out' rings. 'figure' is the sand "
        + "on the plate: a strike throws it off, it settles back along the lines of whichever "
        + "modes are still ringing, and a plate left alone is covered. 'motion' is how much each "
        + "point swings. "
        + "'freq' is the lowest mode, 'aspect' the plate's shape: square, and modes pair up. "
        + "'strike x' and 'strike y' are where it is hit, and the center only wakes the odd modes. "
        + "'brightness' is how much the high modes get and keep. The mode count is set on the "
        + "node, and each mode costs a few ops.")
    {
        Extras = [new ModesExtra()],
        Skin = Art.Skin("plate"),
    };

    /// <summary>The modes each way: 2, 3 or 4, so 4, 9 or 16 in all.</summary>
    private sealed record ModesExtra : NodeExtra
    {
        public const string StateKey = "plate";
        public const string CountKey = "modes";

        public override string Key => StateKey;

        public override IReadOnlyList<ExtraField> Fields =>
        [
            new ExtraField.Choice(CountKey, "modes", Counts, "3"),
        ];

        private static IReadOnlyList<ChoiceOption> Counts { get; } =
        [
            new("2", "2 by 2"),
            new("3", "3 by 3"),
            new("4", "4 by 4"),
        ];

        public override string Report(NodeInstance node)
        {
            var each = Modes(new ExtraState(Fields, node.StateOf(StateKey)));

            return $"It rings {each * each} modes.";
        }
    }

    /// <summary>
    /// Sets how many modes each way an instance rings, for a preset assembling one in
    /// code: what the inspector does when somebody picks from the list.
    /// </summary>
    public static NodeInstance WithModes(NodeInstance node, int each)
    {
        var extra = Definition.Extra<ModesExtra>() ?? new ModesExtra();
        var held = extra.Stored(node.StateOf(extra.Key));

        held[ModesExtra.CountKey] = System.Text.Json.Nodes.JsonValue.Create(Math.Clamp(each, 2, 4).ToString());
        node.SetState(extra.Key, held);

        return node;
    }

    /// <summary>The count this instance carries, held to what the module can build.</summary>
    private static int Modes(ExtraState? state) =>
        state is not null && int.TryParse(state.Chosen(ModesExtra.CountKey), out var each)
            ? Math.Clamp(each, 2, 4)
            : 3;

    private static Slot[] Emit(Emitter em, EmitContext node)
    {
        var each = Modes(node.Extra<ExtraState>(ModesExtra.StateKey));

        // The ring reads no pixel, so a plate Overtones reads at eight places rings once.
        var ring = em.Once(
            "ring",
            [.. node.Inputs[TriggerPort..(StrikeYPort + 1)]],
            () => Ring(em, node, each));

        // The plate's own shape at this pixel, one sine per index each way rather
        // than one per mode, and each once for a row or a column read at several places.
        var hereX = em.Once("column", [node[8]], () =>
        {
            var across = em.Add(em.Binary(OpCode.Div, node[8], em.Mul(em.Load(OpCode.LoadAspect), 2f)), 0.5f);

            return Sines(em, across, each);
        });

        var hereY = em.Once("row", [node[9]], () =>
        {
            // The screen's y is one at the top, and 'strike y' is measured from the top like a row.
            var down = em.Sub(em.Constant(0.5f), em.Mul(node[9], 0.5f));

            return Sines(em, down, each);
        });

        var one = em.Constant(1f);
        var swing = em.Constant(0f);
        var shaking = em.Constant(0f);

        for (var m = 1; m <= each; m++)
        for (var n = 1; n <= each; n++)
        {
            var mode = RingModes + ((m - 1) * each + n - 1) * 2;

            var motion = em.Mul(ring[mode], em.Mul(hereX[m], hereY[n]));
            swing = em.Add(swing, em.Mul(motion, motion));

            var shake = em.Mul(motion, ring[mode + 1]);
            shaking = em.Add(shaking, em.Mul(shake, shake));
        }

        // The level rides on every envelope and on the weight alike, so the divisions
        // take it out and it is put back once. A plate struck on an edge wakes nothing,
        // and every division here answers nought to nought: silent, still, and covered.
        var level = ring[RingLevel];
        var weight = ring[RingWeight];
        var moving = em.Mul(level, em.Binary(OpCode.Div, em.Unary(OpCode.Sqrt, swing), weight));
        var thrown = em.Mul(level, em.Binary(OpCode.Div, em.Unary(OpCode.Sqrt, shaking), weight));
        var sand = em.Sub(one, em.Ternary(OpCode.Smoothstep, em.Constant(Rests), em.Constant(Thrown), thrown));

        return [ring[RingOut], sand, moving];
    }

    /// <summary>sin(iπ·<paramref name="at"/>) for i from 1 to <paramref name="each"/>, at their own index.</summary>
    private static Slot[] Sines(Emitter em, Slot at, int each)
    {
        var sines = new Slot[each + 1];

        for (var i = 1; i <= each; i++)
            sines[i] = em.Unary(OpCode.Sin, em.Mul(at, i * Pi));

        return sines;
    }

    /// <summary>Where <see cref="Ring"/> puts the sound, the level and the weight.</summary>
    private const int RingOut = 0, RingLevel = 1, RingWeight = 2;

    /// <summary>Where <see cref="Ring"/>'s modes start: each is its envelope, then how hard it shakes for its swing.</summary>
    private const int RingModes = 3;

    /// <summary>The strike and every mode's ring: the whole sound, and everything the figure needs but the pixel.</summary>
    private static Slot[] Ring(Emitter em, EmitContext node, int each)
    {
        var one = em.Constant(1f);

        var (age, level) = Strike.Of(em, node[TriggerPort], node[VelocityPort]);

        // The plate's own shape at the strike point, one sine per index each way.
        var strikeX = node[StrikeXPort];
        var strikeY = node[StrikeYPort];

        var struckX = new Slot[each + 1];
        var struckY = new Slot[each + 1];

        for (var i = 1; i <= each; i++)
        {
            // The level rides on one axis of the strike, so it is one multiply for the row rather than one per mode.
            struckX[i] = em.Mul(em.Unary(OpCode.Sin, em.Mul(strikeX, i * Pi)), level);
            struckY[i] = em.Unary(OpCode.Sin, em.Mul(strikeY, i * Pi));
        }

        // f(m, n) = freq * (m² + n² / aspect²) / (1 + 1 / aspect²), so mode (1, 1) is freq.
        var aspect = em.Ternary(OpCode.Clamp, node[AspectPort], one, em.Constant(4f));
        var squeeze = em.Binary(OpCode.Div, one, em.Mul(aspect, aspect));
        var both = em.Add(squeeze, 1f);
        var lowest = em.Binary(OpCode.Div, node[FreqPort], both);

        // Held above nought, so a decay swept to the stop is a short ring and not a division by it.
        var decay = em.Binary(OpCode.Max, node[DecayPort], em.Constant(0.01f));
        var bright = em.Ternary(OpCode.Clamp, node[BrightnessPort], em.Constant(0f), one);
        var dull = em.Sub(one, bright);

        // What each step up the ladder costs a mode in decay, and the two constants folded once for every mode.
        var quicker = em.Sub(one, em.Mul(bright, 0.5f));
        var fall = em.Mul(decay, -1f);
        var spin = em.Mul(lowest, Tau);

        var sound = em.Constant(0f);
        var weight = em.Constant(0f);
        var modes = new (Slot Envelope, Slot Shake)[each * each];

        for (var m = 1; m <= each; m++)
        for (var n = 1; n <= each; n++)
        {
            // How far up the ladder this mode is, nought for the lowest.
            var order = m * m + n * n - 2;

            var amplitude = em.Mul(struckX[m], struckY[n]);

            if (order > 0)
            {
                // Dull plates give the high modes less, and let them go sooner.
                amplitude = em.Binary(OpCode.Div, amplitude, em.Add(em.Mul(dull, order), 1f));
            }

            var rate = em.Binary(OpCode.Div, em.Add(em.Mul(quicker, order), 1f), fall);
            var envelope = em.Mul(amplitude, em.Unary(OpCode.Exp, em.Mul(age, rate)));

            // Where this mode sits on the ladder, the lowest at one.
            var rung = em.Add(em.Mul(squeeze, n * n), m * m);
            var pitch = em.Mul(spin, rung);
            sound = em.Add(sound, em.Mul(envelope, em.Unary(OpCode.Cos, em.Mul(pitch, age))));

            // How hard this mode shakes the plate for its swing: its frequency squared
            // against the lowest mode's, so a high mode counts for far more than its
            // size while it lasts.
            var above = em.Binary(OpCode.Div, rung, both);

            modes[(m - 1) * each + n - 1] = (envelope, em.Mul(above, above));

            weight = em.Add(weight, em.Unary(OpCode.Abs, amplitude));
        }

        Slot[] ring = [em.Mul(level, em.Binary(OpCode.Div, sound, weight)), level, weight];

        return [.. ring, .. modes.SelectMany(mode => new[] { mode.Envelope, mode.Shake })];
    }
}
