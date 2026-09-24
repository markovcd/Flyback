using System.Text.Json.Nodes;
using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Fractals;

/// <summary>
/// z → z² + c until it runs away, unrolled, and the gradient the count is
/// colored with: what Mandelbrot and Julia share.
/// </summary>
/// <remarks>
/// The machine has no loops, so each iteration is a dozen ops of its own, and
/// the count is carried on the node like a Fractal's octaves. An escaped z is
/// frozen rather than iterated on, which keeps every value finite with no branch
/// and leaves the escaped z in hand for the smooth count.
/// </remarks>
internal static class Escape
{
    public const string StateKey = "escape";
    public const string CountKey = "iterations";

    private const string Fallback = "64";

    /// <summary>|z|² past which a point has escaped. Well beyond four, so the smooth count has no seams.</summary>
    private const float Bailout = 256f;

    /// <summary>How far c may be pushed, which bounds every z a frozen point can square.</summary>
    public const float Reach = 64f;

    /// <summary>Iterations for one pass through the colors.</summary>
    private const float Band = 24f;

    private const float Tau = 6.283185307179586f;

    private static readonly int[] Counts = [16, 32, 64, 128, 256];

    /// <summary>The iteration count as a setting on the node.</summary>
    public static NodeExtra Extra { get; } = new SettingsExtra(StateKey,
    [
        new ExtraField.Choice(
            CountKey, "iterations",
            [.. Counts.Select(n => new ChoiceOption(n.ToString(), $"{n} iterations"))],
            Fallback) { Help = "How long each point is run before it counts as in the set. More is finer edges, and slower." },
    ]);

    /// <summary>Sets how many iterations an instance runs, for a preset assembling one in code.</summary>
    public static NodeInstance WithIterations(NodeInstance node, int iterations)
    {
        node.SetState(StateKey, new JsonObject { [CountKey] = JsonValue.Create(Nearest(iterations).ToString()) });

        return node;
    }

    /// <summary>The count this instance carries, held to one the module offers.</summary>
    public static int Iterations(EmitContext node) =>
        int.TryParse(node.Extra<ExtraState>(StateKey)?.Chosen(CountKey), out var count)
            ? Nearest(count)
            : int.Parse(Fallback);

    /// <summary>A point of the plane: the middle plus a position scaled by 'zoom', halving the view each step.</summary>
    public static (Slot X, Slot Y) Plane(Emitter em, Slot x, Slot y, Slot middleX, Slot middleY, Slot zoom, float span)
    {
        var scale = em.Mul(em.Unary(OpCode.Exp, em.Mul(zoom, -MathF.Log(2f))), span);

        return (em.Add(middleX, em.Mul(x, scale)), em.Add(middleY, em.Mul(y, scale)));
    }

    /// <summary>A value held to <see cref="Reach"/>.</summary>
    public static Slot Held(Emitter em, Slot value) =>
        em.Ternary(OpCode.Clamp, value, em.Constant(-Reach), em.Constant(Reach));

    /// <summary>
    /// Runs <paramref name="steps"/> iterations from z and answers the three
    /// readings: the gradient, the smooth count over the most there could be,
    /// and whether it never left. <paramref name="before"/> is iterations already
    /// taken for free.
    /// </summary>
    public static Slot[] Run(
        Emitter em, Slot zx, Slot zy, Slot cx, Slot cy, int steps, int before, Slot shift)
    {
        var zero = em.Constant(0f);
        var one = em.Constant(1f);
        var bailout = em.Constant(Bailout);
        var gone = zero;

        for (var i = 0; i < steps; i++)
        {
            var xx = em.Mul(zx, zx);
            var yy = em.Mul(zy, zy);

            // One from the step z escaped on, so mixing on it holds z where it left.
            var escaped = em.Binary(OpCode.Step, bailout, em.Add(xx, yy));
            gone = em.Add(gone, escaped);

            var nx = em.Add(em.Sub(xx, yy), cx);
            var ny = em.Add(em.Mul(em.Add(zx, zx), zy), cy);

            zx = em.Ternary(OpCode.Mix, nx, zx, escaped);
            zy = em.Ternary(OpCode.Mix, ny, zy, escaped);
        }

        var r2 = em.Add(em.Mul(zx, zx), em.Mul(zy, zy));
        var outside = em.Binary(OpCode.Step, bailout, r2);
        var inside = em.Sub(one, outside);

        // The step it escaped on, less how far past the bailout it landed, in doublings.
        var over = em.Mul(em.Unary(OpCode.Log, r2), 1f / MathF.Log(Bailout));
        var smooth = em.Sub(
            em.Sub(em.Constant(steps + before), gone),
            em.Mul(em.Unary(OpCode.Log, over), 1f / MathF.Log(2f)));
        smooth = em.Binary(OpCode.Max, smooth, zero);

        var most = steps + before;
        var escape = em.Mul(em.Ternary(OpCode.Clamp, em.Mul(smooth, 1f / most), zero, one), outside);

        return [Gradient(em, em.Add(em.Mul(smooth, 1f / Band), shift), outside), escape, inside];
    }

    /// <summary>
    /// Navy, blue, white, gold, near black and round again: one cosine per
    /// channel, each peaking at its own place in the pass, scaled by
    /// <paramref name="level"/>.
    /// </summary>
    public static Slot Gradient(Emitter em, Slot along, Slot level)
    {
        ReadOnlySpan<(float Peak, float Middle, float Swing)> channels =
        [
            (0.58f, 0.42f, 0.62f),
            (0.44f, 0.40f, 0.62f),
            (0.28f, 0.45f, 0.58f),
        ];

        var zero = em.Constant(0f);
        var one = em.Constant(1f);
        var slots = new Slot[3];

        for (var c = 0; c < slots.Length; c++)
        {
            var (peak, middle, swing) = channels[c];

            var wave = em.Unary(OpCode.Cos, em.Mul(em.Add(along, -peak), Tau));
            var channel = em.Ternary(OpCode.Clamp, em.Add(em.Mul(wave, swing), middle), zero, one);

            slots[c] = em.Mul(channel, level);
        }

        return em.Combine(slots[0], slots[1], slots[2]);
    }

    private static int Nearest(int iterations) => Counts.MinBy(n => Math.Abs(n - iterations));
}
