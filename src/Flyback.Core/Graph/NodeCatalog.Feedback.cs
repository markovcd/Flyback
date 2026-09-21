using Flyback.Core.Compile;

namespace Flyback.Core.Graph;

public partial class NodeCatalog
{
    public const string FeedbackTypeId = "feedback";

    private static IEnumerable<NodeDef> Feedback()
    {
        yield return new NodeDef(
            FeedbackTypeId, "Feedback", ModuleCategories.Feedback,
            [..Position()], [Col("color")],
            (em, i) => [em.Triple(OpCode.SampleFeedback, i[0], i[1])],
            "Reads the previous frame. Feed it back through space transforms to make a self-referential loop.")
        {
            Sinks = ModuleSinks.Video,
        };

        yield return Trails();
        yield return Blur();
    }

    /// <summary>
    /// The loop nearly every picture closes with a Feedback: the last frame read
    /// from somewhere slightly else, dimmed, and laid under the new one.
    /// </summary>
    /// <remarks>
    /// Scale, then Rotate, then Translate, in that order and with their arithmetic,
    /// so a knob here means what the same knob means on those three and a patch that
    /// swaps them for this draws the same frame. Each at rest is exact — times one,
    /// a turn of nothing, less nought — so the order costs a patch that uses one of
    /// them nothing. 'x' and 'y' are sockets for the transform this does not have:
    /// a Warp patched in bends the tail.
    /// </remarks>
    private static NodeDef Trails()
    {
        const int zoom = 3;
        const int angle = 4;
        const int dx = 5;
        const int dy = 6;
        const int persist = 7;

        return new NodeDef(
            "feedback.trails", "Trails", ModuleCategories.Feedback,
            [
                Col("in"),
                ..Position(),
                Num("zoom", 1f, 0.5f, 2f),
                Num("angle", 0f, -0.5f, 0.5f),
                Num("dx", 0f, -0.1f, 0.1f),
                Num("dy", 0f, -0.1f, 0.1f),
                Num("persist", 0.9f, 0f, 1f),
            ],
            [Col("color"), Col("tail")],
            (em, i) =>
            {
                var x = em.Mul(i[1], i[zoom]);
                var y = em.Mul(i[2], i[zoom]);

                var cos = em.Unary(OpCode.Cos, i[angle]);
                var sin = em.Unary(OpCode.Sin, i[angle]);
                var turnedX = em.Binary(OpCode.Sub, em.Mul(x, cos), em.Mul(y, sin));
                var turnedY = em.Binary(OpCode.Add, em.Mul(x, sin), em.Mul(y, cos));

                var before = em.Triple(
                    OpCode.SampleFeedback,
                    em.Binary(OpCode.Sub, turnedX, i[dx]),
                    em.Binary(OpCode.Sub, turnedY, i[dy]));

                var tail = em.Mul(before, i[persist]);

                return [em.Binary(OpCode.Max, tail, i[0]), tail];
            },
            "Leaves a trail behind whatever is patched into 'in'. The last frame is read back "
            + "through 'zoom', 'angle', 'dx' and 'dy', dimmed by 'persist', and the brighter of "
            + "that and the new picture comes out of 'color' — patch it into the Output. 'zoom' "
            + "just over 1 draws the trail inward and just under pushes it out; a small 'angle' "
            + "makes it spiral; 'dx' and 'dy' smear it sideways. 'persist' is how long it lasts: "
            + "0.8 is a short ghost, 0.98 takes seconds to fade. Small numbers go a long way, "
            + "since they are applied again every frame. 'tail' is the dimmed last frame alone, "
            + "for a Blend or a Layer instead of the brighter-of. Patch a Warp into 'x' and 'y' "
            + "to bend the trail.")
        {
            Sinks = ModuleSinks.Video,
        };
    }

    /// <summary>
    /// The last frame read at nine places at once and averaged, which is a blur
    /// taken a pass per frame rather than all at once.
    /// </summary>
    /// <remarks>
    /// Blurring the frame being drawn is the one thing a single pass cannot do —
    /// a pixel may read the frame before at any coordinate and the current one
    /// nowhere but where it is (ADR-0012). So the kernel goes round the loop
    /// instead: one pass is the separable [1 2 1] tent, barely a pixel wide at
    /// the default radius, and what makes it a blur is the passes piling up.
    /// The price is that what is softened is a frame behind, so anything moving
    /// smears as well.
    /// <para>
    /// The kernel sums to one and 'amount' is a mix rather than a gain, so the
    /// pile settles instead of growing without end: how much of each pass
    /// survives into the next is what sets the width it settles at. Clamped,
    /// because past one the mix extrapolates and the loop has nothing pulling it
    /// back.
    /// </para>
    /// </remarks>
    private static NodeDef Blur()
    {
        const int radius = 3;
        const int amount = 4;

        return new NodeDef(
            "feedback.blur", "Blur", ModuleCategories.Feedback,
            [
                Col("in"),
                ..Position(),
                Num("radius", 0.02f, 0f, 0.1f),
                Num("amount", 0.7f, 0f, 1f),
            ],
            [Col("color"), Col("soft")],
            (em, i) =>
            {
                var left = em.Sub(i[1], i[radius]);
                var right = em.Add(i[1], i[radius]);
                var below = em.Sub(i[2], i[radius]);
                var above = em.Add(i[2], i[radius]);

                Slot Tap(Slot x, Slot y) => em.Triple(OpCode.SampleFeedback, x, y);

                var middle = Tap(i[1], i[2]);

                var sides = em.Add(
                    em.Add(Tap(left, i[2]), Tap(right, i[2])),
                    em.Add(Tap(i[1], below), Tap(i[1], above)));

                var corners = em.Add(
                    em.Add(Tap(left, below), Tap(right, below)),
                    em.Add(Tap(left, above), Tap(right, above)));

                var soft = em.Mul(
                    em.Add(em.Add(em.Mul(middle, 4f), em.Mul(sides, 2f)), corners), 1f / 16f);

                var mix = em.Ternary(OpCode.Clamp, i[amount], em.Constant(0f), em.Constant(1f));

                return [em.Ternary(OpCode.Mix, i[0], soft, mix), soft];
            },
            "Softens the picture. The last frame is read at nine places around each pixel and "
            + "averaged, so what comes out of 'color' is blurrier than what went into 'in' — "
            + "patch it into the Output, since the loop closes through the screen and nothing "
            + "happens if the result never gets there. 'radius' is how far apart the nine "
            + "readings sit, in the same units as the coordinates, and one frame of it is a "
            + "small blur; the width you actually see is set by 'amount', which is how much of "
            + "each pass survives into the next. 0 is a wire, 0.7 is a soft picture, 0.95 is a "
            + "fog, and 1 never lets the new picture in at all and dissolves what is there. It "
            + "takes a few frames to arrive and anything moving smears as well as softens, "
            + "because the frame being blurred is the one before. 'soft' is the average on its "
            + "own, for laying back over the original with a Layer. The nine readings move "
            + "together, so a Warp patched into 'x' and 'y' drags the softness in a direction "
            + "as it spreads rather than stretching it.")
        {
            Sinks = ModuleSinks.Video,
        };
    }
}
