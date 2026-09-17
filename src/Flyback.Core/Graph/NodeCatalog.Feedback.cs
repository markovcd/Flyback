using Flyback.Core.Compile;

namespace Flyback.Core.Graph;

public partial class NodeCatalog
{
    private static IEnumerable<NodeDef> Feedback()
    {
        yield return new NodeDef(
            "feedback", "Feedback", ModuleCategories.Feedback,
            [..Position()], [Col("color")],
            (em, i) => [em.Triple(OpCode.SampleFeedback, i[0], i[1])],
            "Reads the previous frame. Feed it back through space transforms to make a self-referential loop.")
        {
            Sinks = ModuleSinks.Video,
        };

        yield return Trails();
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
}
