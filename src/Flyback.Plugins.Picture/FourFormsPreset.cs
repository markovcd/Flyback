using Flyback.Core.Graph;

namespace Flyback.Plugins.Picture;

/// <summary>
/// All four forms on a ring, turning, with the seam between them opening and
/// closing — so they are four shapes and then one, and then four again.
/// </summary>
/// <remarks>
/// The plugin's showcase, and it is built round the one gesture none of the
/// modules can make alone. A Minimum has been in the catalogue since the first
/// week and would put these four in one picture perfectly well; what it cannot do
/// is the crease. Where two forms meet under a Minimum there is a corner, and
/// four shapes sharing corners look like four shapes overlapping. The Combine's
/// seam fills that corner in, and swept from nothing to half a unit — a quarter
/// of the picture — it walks the whole way from four separate things to one and
/// back again, which is a topology changing on a knob and is worth watching for
/// its own sake. What is left in the middle at the top of the sweep is a hole,
/// because four forms on a ring that reach each other still do not reach the
/// centre. That is the arrangement rather than an accident of it: the ring is set
/// so the four close on their neighbours and not on the middle.
/// <para>
/// That signal used to do one other thing: it was a Pulse's width, so what the
/// eye saw as four shapes flowing together the ear heard as a thin buzz opening
/// into a hollow square. It has gone, and the reason is worth keeping. The seam
/// is a fact about distance fields and the duty cycle is a fact about a
/// waveform, and the two were related only by having been handed the same
/// number. A patch that shares a signal between the sinks is showing that the
/// two sinks are one graph; a patch that shares a signal between two unrelated
/// things is showing that a number can be sent to two places, which nobody
/// needed telling.
/// </para>
/// <para>
/// The forms sit on a ring rather than in a row because a row would need the
/// whole width of the frame and would leave the picture the shape of the window.
/// A ring is centred, so it reads the same on a square preview and a wide one.
/// It rocks rather than spins: turned through a whole revolution the four would
/// swap places, and the eye reads that as the composition jumping rather than as
/// the composition turning.
/// </para>
/// </remarks>
internal static class FourFormsPreset
{
    public const string Name = "Four forms";

    /// <summary>How far each form sits from the middle.</summary>
    private const float Ring = 0.4f;

    public static Patch Build(ModuleCatalog modules)
    {
        var b = new PatchBuilder(modules);

        // The two sweeps, and neither is heard or seen directly.
        var rock = b.Add("osc.sine", 80, 200, (1, 0.05f), (3, 1.2f));
        var melt = b.Add("osc.sine", 80, 900, (1, 0.09f), (3, 0.24f), (4, 0.26f));

        // Here for the same reason the Supersaw preset has a Coordinates: 'x' and
        // 'y' are normalled to the pixel's own position, so reading a form
        // somewhere else takes a wire (ADR-0050). One Rotate ahead of the four
        // turns the whole arrangement rather than each shape in place.
        var turn = b.Add("space.rotate", 320, 420);

        var forms = new[]
        {
            Place(b, 0, -Ring, Ring, CircleModule.TypeId, (2, 0.28f)),
            Place(b, 1, Ring, Ring, BoxModule.TypeId, (2, 0.28f), (3, 0.26f), (4, 0.06f)),
            Place(b, 2, Ring, -Ring, PolygonModule.TypeId, (2, 0.3f), (3, 6f)),
            Place(b, 3, -Ring, -Ring, StarModule.TypeId, (2, 0.34f), (3, 5f), (4, 0.45f)),
        };

        b.Wire(rock, 0, turn, 2);

        foreach (var (move, shape) in forms)
        {
            b.Wire(turn, 0, move, 0)
             .Wire(turn, 1, move, 1)
             .Wire(move, 0, shape, 0)
             .Wire(move, 1, shape, 1);
        }

        b.Group("Sweeps", rock, melt, turn);

        string[] formNames = ["Circle", "Box", "Polygon", "Star"];

        for (var i = 0; i < forms.Length; i++)
            b.Group(formNames[i], forms[i].Move, forms[i].Shape);

        // Chained, because a Combine takes two: three of them for four forms, and
        // the field coming out of one is a distance like the ones going in, which
        // is the whole reason they chain at all.
        var merged = forms[0].Shape;
        var combines = new List<NodeInstance>();

        for (var i = 1; i < forms.Length; i++)
        {
            var combine = b.Add(CombineModule.TypeId, 1060, 120 + i * 200f);

            b.Wire(merged, 0, combine, 0)
             .Wire(forms[i].Shape, 0, combine, 1)
             .Wire(melt, 0, combine, 2);

            merged = combine;
            combines.Add(combine);
        }

        b.Group("Merge", [.. combines]);

        var ink = b.Add(FillModule.TypeId, 1320, 320, (1, 0.006f), (2, 0.016f));

        // Eye: the fill lit in a color the same sweep chooses, with its own
        // outline over the top — white, because it is added to a color rather
        // than being one.
        var tint = b.Add("color.hsv", 1560, 260, (1, 0.75f));
        var lit = b.Add("math.add", 1780, 340);

        var output = b.Add(
            NodeCatalog.OutputTypeId, 2020, 520, (NodeCatalog.OutputGainPort, 0.4f));

        b.Wire(merged, 0, ink, 0)
         .Wire(melt, 0, tint, 0)
         .Wire(ink, 0, tint, 2)
         .Wire(tint, 0, lit, 0)
         .Wire(ink, 1, lit, 1)
         .Wire(lit, 0, output, NodeCatalog.OutputColorPort);

        b.Group("Eye", ink, tint, lit);

        return b.Patch;
    }

    /// <summary>One form and the Translate that puts it where it belongs.</summary>
    private static (NodeInstance Move, NodeInstance Shape) Place(
        PatchBuilder b, int row, float dx, float dy, string typeId,
        params (int Port, float Value)[] knobs)
    {
        var y = 80 + row * 200f;

        return (b.Add("space.translate", 560, y, (2, dx), (3, dy)), b.Add(typeId, 800, y, knobs));
    }
}
