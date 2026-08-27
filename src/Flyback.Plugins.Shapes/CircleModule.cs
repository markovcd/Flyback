using Flyback.Core.Compile;
using Flyback.Core.Graph;

namespace Flyback.Plugins.Shapes;

/// <summary>
/// The distance to a circle: how far this point is from the rim, negative
/// inside it.
/// </summary>
/// <remarks>
/// Two ops, and it is here anyway. Everything else in the plugin is a harder
/// version of this one line, so it is what the convention is easiest to read
/// off — and a catalogue in which the simplest possible form has to be assembled
/// by hand out of a Length and a Subtract is one where nobody finds the rest.
/// </remarks>
internal static class CircleModule
{
    public const string TypeId = "flyback.shapes.circle";

    public static NodeDef Definition { get; } = new(
        TypeId, "Circle", ShapesPlugin.Category,
        [..Field.Position(), Field.Size("radius", 0.5f)],
        [Field.Distance("distance")],
        Emit,
        "A circle, as the distance to its rim: negative inside, zero on the edge, positive "
        + "outside. Patch it into a Fill to see it. It is exact everywhere, which makes it "
        + "the one to reach for when a Combine is going to smooth it against something else.");

    private static Slot[] Emit(Emitter em, EmitContext node) =>
        [em.Sub(em.Binary(OpCode.Hypot, node[0], node[1]), node[2])];
}
