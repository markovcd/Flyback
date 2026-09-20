using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// The layout: where the modules go, so that a patch reads left to right and
/// its wires can be followed.
/// </summary>
/// <remarks>
/// Everything here is a property of the drawing rather than a set of expected
/// coordinates, because a coordinate is a thing that changes when a gap is
/// widened and a property is not. What the drawing has to be is: forward,
/// non-overlapping, unchanged as a program, and the same answer twice.
/// </remarks>
public class PatchLayoutTests
{
    private static readonly PatchLayout.Metrics Size = PatchLayout.Metrics.Default;

    private static Patch Preset(string name) =>
        Presets.All.Single(p => p.Name == name).Build(NodeCatalog.BuiltIn);

    public static TheoryData<string> EveryPreset =>
        [.. Presets.All.Where(p => p.Name != "Empty").Select(p => p.Name)];

    private static Patch Arranged(Patch patch)
    {
        PatchLayout.Arrange(patch, NodeCatalog.BuiltIn);
        return patch;
    }

    /// <summary>One rectangle of the drawing, named so that a failure says which.</summary>
    private readonly record struct Drawn(string What, double Left, double Top, double Right, double Bottom);

    private static Drawn Box(NodeInstance node)
    {
        var def = NodeCatalog.BuiltIn.Require(node.TypeId);
        return new Drawn(node.TypeId, node.X, node.Y, node.X + Size.Width, node.Y + Size.Height(def));
    }

    /// <summary>
    /// The box a shut group is drawn as: at the corner of the modules it stands
    /// for, one module wide, and a row tall for every socket on its edge.
    /// </summary>
    private static Drawn Box(Patch patch, NodeGroup group)
    {
        var left = group.Members.Min(id => patch.Find(id)!.X);
        var top = group.Members.Min(id => patch.Find(id)!.Y);

        return new Drawn(
            group.Title(),
            left,
            top,
            left + Size.Width,
            top + Size.GroupHeight(patch.SocketsOf(group)));
    }

    /// <summary>Where a module is drawn, which is the box in front of it where there is one.</summary>
    private static Drawn Where(Patch patch, NodeInstance node) =>
        patch.CollapsedGroupOf(node.Id) is { } group ? Box(patch, group) : Box(node);

    /// <summary>
    /// Everything the canvas has on it, which is what every property here is
    /// about. A module behind a shut box is not one of them: nothing paints it
    /// and nothing points at it, so the layout parks it behind the box rather
    /// than keeping a column open for a picture nobody is looking at.
    /// </summary>
    private static List<Drawn> Drawing(Patch patch)
    {
        var drawn = new List<Drawn>();

        foreach (var group in patch.Groups ?? [])
            if (group.Collapsed)
                drawn.Add(Box(patch, group));

        foreach (var node in patch.Nodes)
            if (patch.CollapsedGroupOf(node.Id) is null)
                drawn.Add(Box(node));

        return drawn;
    }

    private static void NothingOverlaps(Patch patch)
    {
        var drawn = Drawing(patch);

        for (var a = 0; a < drawn.Count; a++)
        for (var b = a + 1; b < drawn.Count; b++)
        {
            var (one, two) = (drawn[a], drawn[b]);

            var apart = one.Right <= two.Left || two.Right <= one.Left
                || one.Bottom <= two.Top || two.Bottom <= one.Top;

            apart.ShouldBeTrue($"{one.What} and {two.What} overlap");
        }
    }

    /// <summary>
    /// Every wire leaves a node to the left of the one it arrives at, with clear
    /// space in between. A wire that ran backwards would have to double round
    /// the module it came from, which is the one thing a signal chain must never
    /// look like.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryPreset))]
    public void Every_wire_runs_forwards(string name)
    {
        var patch = Arranged(Preset(name));

        foreach (var wire in patch.Connections)
        {
            var from = patch.Find(wire.SourceNode).ShouldNotBeNull();
            var to = patch.Find(wire.TargetNode).ShouldNotBeNull();

            // Unless it is the wire that closes a loop, which is the one wire
            // meant to be read as going back — and is drawn dashed to say so.
            if (Cycles.Backwards(patch).Contains(wire)) continue;

            // Or unless one box stands in front of both ends, in which case
            // there is no wire on the canvas to run either way.
            if (patch.CollapsedGroupOf(from.Id) is { } box
                && ReferenceEquals(box, patch.CollapsedGroupOf(to.Id)))
                continue;

            var (start, end) = (Where(patch, from), Where(patch, to));

            start.Right.ShouldBeLessThanOrEqualTo(
                end.Left, $"{start.What} feeds {end.What} and should sit to its left");
        }
    }

    [Theory]
    [MemberData(nameof(EveryPreset))]
    public void No_two_modules_overlap(string name) => NothingOverlaps(Arranged(Preset(name)));

    /// <summary>
    /// And still none of them overlap once every group has been taken off, which is
    /// what a person does to a patch they have been handed.
    /// </summary>
    /// <remarks>
    /// Its own case because it is a much larger drawing: ten boxes become a hundred
    /// modules, which is wide enough to reach the end of the canvas — where
    /// <see cref="NodeInstance.X"/> holds them, one on top of another.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryPreset))]
    public void No_two_modules_overlap_once_the_groups_are_off(string name)
    {
        var patch = Preset(name);
        patch.Groups = null;

        NothingOverlaps(Arranged(patch));
    }

    /// <summary>
    /// Opening every group is the same again, and the harder half of it: an open
    /// group is drawn as a ring round its modules, so it takes the room they take
    /// rather than the room a box takes.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryPreset))]
    public void No_two_modules_overlap_once_the_groups_are_open(string name)
    {
        var patch = Preset(name);

        foreach (var group in patch.Groups ?? []) group.Collapsed = false;

        NothingOverlaps(Arranged(patch));
    }

    /// <summary>
    /// A drawing no bigger than the canvas is put on the canvas.
    /// </summary>
    /// <remarks>
    /// Coordinates are held inside the canvas, so a drawing that runs past the
    /// edge arrives folded onto it with everything beyond stacked on the
    /// boundary. Using the whole canvas rather than the quarter below and right
    /// of the origin is what keeps a large patch off it.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryPreset))]
    public void A_drawing_that_fits_the_canvas_is_put_on_it(string name)
    {
        var patch = Preset(name);
        patch.Groups = null;

        PatchLayout.Arrange(patch, NodeCatalog.BuiltIn).Fitted.ShouldBeTrue($"'{name}' should fit");

        foreach (var drawn in Drawing(patch))
        {
            drawn.Left.ShouldBeGreaterThan(-NodeInstance.Across);
            drawn.Right.ShouldBeLessThan(NodeInstance.Across);
            drawn.Top.ShouldBeGreaterThan(-NodeInstance.Down);
            drawn.Bottom.ShouldBeLessThan(NodeInstance.Down);
        }
    }

    /// <summary>
    /// The Output is the end of the patch and reads as the end. It is pinned
    /// there rather than left to the arithmetic, which would put a Probe hanging
    /// off a long chain further right than the sink it is watching.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryPreset))]
    public void The_output_is_the_rightmost_module(string name)
    {
        var patch = Arranged(Preset(name));
        var sink = patch.FirstOf(NodeCatalog.OutputTypeId).ShouldNotBeNull();

        foreach (var drawn in Drawing(patch))
            drawn.Left.ShouldBeLessThanOrEqualTo(sink.X, $"{drawn.What} is drawn past the Output");
    }

    /// <summary>
    /// Nothing but coordinates changes, which is what makes the button safe to
    /// press: the patch compiles to the same instructions, so the picture and
    /// the sound are untouched.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryPreset))]
    public void Laying_out_a_patch_does_not_change_what_it_compiles_to(string name)
    {
        var before = Preset(name).CompileForVideo(NodeCatalog.BuiltIn).Program.Ops;
        var after = Arranged(Preset(name)).CompileForVideo(NodeCatalog.BuiltIn).Program.Ops;

        after.ShouldBe(before);
    }

    /// <summary>
    /// Laying out an already laid-out patch changes nothing. Pressing the button
    /// twice should not shuffle the canvas, and it is the property a relaxation
    /// could not have given — see ADR-0044.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryPreset))]
    public void Laying_out_twice_is_laying_out_once(string name)
    {
        var patch = Arranged(Preset(name));
        var settled = patch.Nodes.ToDictionary(n => n.Id, n => (n.X, n.Y));

        PatchLayout.Arrange(patch, NodeCatalog.BuiltIn);

        foreach (var node in patch.Nodes)
            (node.X, node.Y).ShouldBe(settled[node.Id], $"{node.TypeId} moved on the second pass");
    }

    /// <summary>
    /// A preset arrives placed, without anybody laying it out first. It declares
    /// no coordinates of its own (ADR-0070), so a preset that had not been
    /// through the layout would hand the canvas a pile at the origin — which is
    /// every module overlapping every other one.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryPreset))]
    public void A_preset_arrives_laid_out(string name) => NothingOverlaps(Preset(name));

    /// <summary>
    /// A drawing wider than the canvas with its boxes open has boxes shut until it
    /// fits, and says which (ADR-0092).
    /// </summary>
    /// <remarks>
    /// Eight chains of ten, each in a box of its own: open, each box is a ring round
    /// ten columns of its own, and eight of those in a row want some twenty-five
    /// thousand units against a canvas of fifteen. Shut, each is one module wide.
    /// </remarks>
    [Fact]
    public void A_drawing_too_wide_with_its_boxes_open_has_boxes_shut_until_it_fits()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);
        var output = b.Add(NodeCatalog.OutputTypeId);
        NodeInstance? last = null;

        for (var chain = 0; chain < 8; chain++)
        {
            var made = new List<NodeInstance>();

            for (var i = 0; i < 10; i++)
            {
                var node = b.Add("math.mul", (1, 0.9f));

                if (last is { } feeding) b.Wire(feeding, 0, node, 0);

                made.Add(node);
                last = node;
            }

            b.Group($"Chain {chain}", [.. made]);
        }

        b.Wire(last!, 0, output, NodeCatalog.OutputLeftPort);

        foreach (var group in b.Patch.Groups!) group.Collapsed = false;

        var laid = PatchLayout.Arrange(b.Patch, NodeCatalog.BuiltIn);

        laid.Fitted.ShouldBeTrue("boxes should have been shut until the drawing fitted");
        laid.Shut.ShouldNotBeEmpty();

        foreach (var group in laid.Shut) group.Collapsed.ShouldBeTrue();

        NothingOverlaps(b.Patch);

        // And laying out the result is still laying out nothing: a drawing that had
        // to have boxes shut is one that fits once they are, so the second press
        // finds a patch it has no quarrel with.
        var settled = b.Patch.Nodes.ToDictionary(node => node.Id, node => (node.X, node.Y));
        var again = PatchLayout.Arrange(b.Patch, NodeCatalog.BuiltIn);

        again.Fitted.ShouldBeTrue();
        again.Shut.ShouldBeEmpty();

        foreach (var node in b.Patch.Nodes)
            (node.X, node.Y).ShouldBe(settled[node.Id], $"{node.TypeId} moved on the second pass");
    }

    /// <summary>
    /// A patch too big to draw with nothing left to shut is left exactly where it
    /// was, rather than written down and folded onto the boundary (ADR-0092).
    /// </summary>
    /// <remarks>
    /// Two hundred modules wired to nothing is one column two hundred deep, which is
    /// three times the canvas — and no box to shut, since there are no boxes.
    /// </remarks>
    [Fact]
    public void A_patch_too_big_to_draw_is_left_exactly_where_it_was()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        for (var i = 0; i < 200; i++) b.Add("pattern.noise", i * 7d, i * 11d);

        var before = b.Patch.Nodes.Select(node => (node.X, node.Y)).ToArray();
        var laid = PatchLayout.Arrange(b.Patch, NodeCatalog.BuiltIn);

        laid.Fitted.ShouldBeFalse();
        laid.Shut.ShouldBeEmpty();

        b.Patch.Nodes.Select(node => (node.X, node.Y)).ShouldBe(before);
    }

    /// <summary>
    /// A module wired to nothing goes before the first column rather than among
    /// the sources, which it is not one of.
    /// </summary>
    [Fact]
    public void A_module_wired_to_nothing_is_parked_ahead_of_the_patch()
    {
        var patch = Preset("Drone");
        var stray = NodeInstance.Create(NodeCatalog.BuiltIn.Require("pattern.noise"), 9999, 9999);
        patch.Nodes.Add(stray);

        PatchLayout.Arrange(patch, NodeCatalog.BuiltIn);

        foreach (var node in patch.Nodes.Where(n => n.Id != stray.Id))
            stray.X.ShouldBeLessThan(node.X);
    }

    /// <summary>
    /// A patch holding a loop lays out rather than hanging. The wire that closes
    /// it leaves a cycle breaker, and those are the edges the layout cuts before
    /// it counts anything.
    /// </summary>
    [Fact]
    public void A_patch_with_a_loop_in_it_still_lays_out()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var time = b.Add("time", 0, 0);
        var osc = b.Add("osc.sine", 0, 0, (1, 220f));
        var mix = b.Add("math.add", 0, 0);
        var half = b.Add("math.mul", 0, 0, (1, 0.5f));
        var output = b.Add(NodeCatalog.OutputTypeId, 0, 0);

        b.Wire(time, 0, osc, 0)
         .Wire(osc, 0, mix, 0)
         .Wire(mix, 0, half, 0)
         .Wire(half, 0, mix, 1)
         .Wire(mix, 0, output, NodeCatalog.OutputLeftPort);

        PatchLayout.Arrange(b.Patch, NodeCatalog.BuiltIn);

        // The loop is cut where it leaves the module nearest the Output, so the
        // wire that still reads forwards is the one into the mix — and the half
        // sits to its left, where what feeds a module goes.
        Cycles.Backwards(b.Patch).ShouldHaveSingleItem().SourceNode.ShouldBe(mix.Id);

        b.Patch.Find(half.Id)!.X.ShouldBeLessThan(b.Patch.Find(mix.Id)!.X);
        b.Patch.Nodes.Select(n => n.X).Distinct().Count().ShouldBeGreaterThan(1);
    }

    /// <summary>
    /// Wires that have to cross are the whole reason the ordering pass exists,
    /// so it is worth pinning that it does something. Two chains fed in the
    /// order that crosses them lay out uncrossed.
    /// </summary>
    [Fact]
    public void Two_chains_wired_across_each_other_are_untangled()
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var time = b.Add("time", 0, 0);
        var top = b.Add("osc.sine", 0, 0, (1, 220f));
        var bottom = b.Add("osc.sine", 0, 0, (1, 330f));

        // Added in the order that puts the crossing in: the first multiplier
        // takes the second oscillator and the second takes the first.
        var first = b.Add("math.mul", 0, 0);
        var second = b.Add("math.mul", 0, 0);
        var sum = b.Add("math.add", 0, 0);
        var output = b.Add(NodeCatalog.OutputTypeId, 0, 0);

        b.Wire(time, 0, top, 0)
         .Wire(time, 0, bottom, 0)
         .Wire(bottom, 0, first, 0)
         .Wire(top, 0, second, 0)
         .Wire(first, 0, sum, 0)
         .Wire(second, 0, sum, 1)
         .Wire(sum, 0, output, NodeCatalog.OutputLeftPort);

        PatchLayout.Arrange(b.Patch, NodeCatalog.BuiltIn);

        // Whichever oscillator ends up on top, its multiplier is on top too.
        var topper = b.Patch.Find(top.Id)!.Y < b.Patch.Find(bottom.Id)!.Y ? second : first;
        var other = topper.Id == second.Id ? first : second;

        b.Patch.Find(topper.Id)!.Y.ShouldBeLessThan(b.Patch.Find(other.Id)!.Y);
    }

    /// <summary>
    /// A module whose plugin is not installed cannot be measured, so it is left
    /// exactly where the file put it rather than moved to a guessed size.
    /// </summary>
    [Fact]
    public void A_module_that_is_not_in_the_catalogue_is_left_alone()
    {
        var patch = Preset("Drone");
        // Somewhere arbitrary, and inside the canvas: a coordinate outside it
        // would be held to the edge on its way in and this would then be a test
        // of the bound rather than of the layout.
        var unknown = new NodeInstance { Id = Guid.NewGuid(), TypeId = "nobody.knows", X = 1234, Y = 2678 };
        patch.Nodes.Add(unknown);

        PatchLayout.Arrange(patch, NodeCatalog.BuiltIn);

        unknown.X.ShouldBe(1234);
        unknown.Y.ShouldBe(2678);
    }

    /// <summary>
    /// A group is one thing on the canvas, so it is one thing in the layout: the
    /// box goes in a column of its own, after what feeds it and before what
    /// reads it. Placed a module at a time instead, its modules go down whichever
    /// columns their own wires ask for and the box is drawn at the corner of
    /// whichever of them landed furthest up and left — which is a box sitting on
    /// a module that is not in it.
    /// </summary>
    [Fact]
    public void A_shut_group_is_placed_as_the_one_box_it_is_drawn_as()
    {
        var patch = Straggling(out var group, out var stranger, shut: true);
        var box = Box(patch, group);

        // Past the module that feeds it, since that is a column of its own and
        // the box is a column of its own after it.
        box.Left.ShouldBeGreaterThanOrEqualTo(Box(stranger).Right);

        NothingOverlaps(patch);
    }

    /// <summary>
    /// The ring round a group that is open holds its own modules and nothing
    /// else. It is drawn from wherever they sit, so modules scattered down the
    /// patch are a ring drawn down the patch — with whatever was in the way
    /// inside it, looking for all the world like part of the group.
    /// </summary>
    [Fact]
    public void An_open_group_is_drawn_round_its_own_modules_and_no_others()
    {
        var patch = Straggling(out var group, out _, shut: false);

        var members = group.Members.Select(id => patch.Find(id)!).ToArray();
        var pad = Size.GroupPadding;

        var ring = new Drawn(
            group.Title(),
            members.Min(n => n.X) - pad,
            members.Min(n => n.Y) - pad,
            members.Max(n => n.X + Size.Width) + pad,
            members.Max(n => n.Y + Size.Height(NodeCatalog.BuiltIn.Require(n.TypeId))) + pad);

        foreach (var node in patch.Nodes.Where(n => !group.Members.Contains(n.Id)))
        {
            var box = Box(node);

            var apart = box.Right <= ring.Left || ring.Right <= box.Left
                || box.Bottom <= ring.Top || ring.Bottom <= box.Top;

            apart.ShouldBeTrue($"{node.TypeId} is drawn inside a group it is not in");
        }
    }

    /// <summary>
    /// A group whose two modules are one step and three steps along the chain, with a
    /// module belonging to nobody in the step between them.
    /// </summary>
    /// <remarks>
    /// The arrangement that catches a group placed a module at a time: its two
    /// modules go into two columns with a stranger's in the middle, so whatever is
    /// drawn round them reaches across all three.
    /// </remarks>
    private static Patch Straggling(out NodeGroup group, out NodeInstance stranger, bool shut)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var time = b.Add("time", 0, 0);
        var head = b.Add("osc.sine", 0, 0, (1, 220f));
        var tail = b.Add("math.add", 0, 0);

        var apart = b.Add("osc.sine", 0, 0, (1, 330f));
        stranger = b.Add("math.mul", 0, 0);

        var sink = b.Add(NodeCatalog.OutputTypeId, 0, 0);

        b.Wire(time, 0, head, 0)
         .Wire(time, 0, apart, 0)
         .Wire(apart, 0, stranger, 0)
         .Wire(head, 0, tail, 0)
         .Wire(stranger, 0, tail, 1)
         .Wire(tail, 0, sink, NodeCatalog.OutputLeftPort);

        b.Group("Ends", head, tail);

        group = b.Patch.Groups.ShouldNotBeNull().ShouldHaveSingleItem();
        group.Collapsed = shut;

        PatchLayout.Arrange(b.Patch, NodeCatalog.BuiltIn);

        return b.Patch;
    }

    [Fact]
    public void An_empty_patch_lays_out_without_complaint() =>
        Should.NotThrow(() => PatchLayout.Arrange(new Patch(), NodeCatalog.BuiltIn));

    // --- laying out part of a patch -----------------------------------------

    /// <summary>
    /// A chain of three fed by one module, with a second chain of two well away
    /// from it. Enough that laying out either one alone has something to do and
    /// something to leave alone.
    /// </summary>
    private static Patch Apart(out NodeInstance[] near, out NodeInstance[] far)
    {
        var b = new PatchBuilder(NodeCatalog.BuiltIn);

        var time = b.Add("time", 400, -900);
        var osc = b.Add("osc.sine", -800, -400, (1, 220f));
        var gain = b.Add("math.mul", -200, -1200, (1, 0.5f));

        var coord = b.Add("coord", 2400, 1600);
        var rings = b.Add("pattern.rings", 1800, 2200);

        var sink = b.Add(NodeCatalog.OutputTypeId, 3000, 0);

        b.Wire(time, 0, osc, 0)
         .Wire(osc, 0, gain, 0)
         .Wire(gain, 0, sink, NodeCatalog.OutputLeftPort)
         .Wire(coord, 0, rings, 0)
         .Wire(rings, 0, sink, NodeCatalog.OutputColorPort);

        near = [time, osc, gain];
        far = [coord, rings];

        return b.Patch;
    }

    private static HashSet<Guid> Named(params NodeInstance[] nodes) => [.. nodes.Select(n => n.Id)];

    /// <summary>
    /// A layout given part of a patch moves that part and nothing else. What it
    /// is for is a corner of a big patch that has gone tangled, where laying the
    /// whole thing out would throw away every placement elsewhere.
    /// </summary>
    [Fact]
    public void Laying_out_part_of_a_patch_moves_only_the_modules_it_names()
    {
        var patch = Apart(out var near, out var far);
        var before = patch.Nodes.ToDictionary(n => n.Id, n => (n.X, n.Y));

        PatchLayout.Arrange(patch, NodeCatalog.BuiltIn, null, Named(near));

        foreach (var node in far)
            (node.X, node.Y).ShouldBe(before[node.Id], $"{node.TypeId} was not named");

        near.ShouldContain(
            n => n.X != before[n.Id].X || n.Y != before[n.Id].Y,
            "the named modules should have moved");
    }

    /// <summary>And what it does move reads left to right, the same as a whole patch does.</summary>
    [Fact]
    public void The_part_that_is_laid_out_reads_left_to_right()
    {
        var patch = Apart(out var near, out _);

        PatchLayout.Arrange(patch, NodeCatalog.BuiltIn, null, Named(near));

        var (time, osc, gain) = (near[0], near[1], near[2]);

        time.X.ShouldBeLessThan(osc.X);
        osc.X.ShouldBeLessThan(gain.X);
    }

    /// <summary>
    /// And it lands where it was rather than in the middle of the canvas, which
    /// is what keeps the part being worked on under the view it is being worked
    /// on through.
    /// </summary>
    [Fact]
    public void The_part_that_is_laid_out_stays_where_it_was()
    {
        var patch = Apart(out _, out var far);

        var was = Middle(far);

        PatchLayout.Arrange(patch, NodeCatalog.BuiltIn, null, Named(far));

        var (x, y) = Middle(far);

        // A column of two where there were two scattered: the middle moves by
        // the room the drawing took back, and not by the half-canvas that
        // landing it in the centre would have cost.
        Math.Abs(x - was.X).ShouldBeLessThan(Size.Width);
        Math.Abs(y - was.Y).ShouldBeLessThan(Size.Height(NodeCatalog.BuiltIn.Require("coord")));
    }

    private static (double X, double Y) Middle(NodeInstance[] nodes)
    {
        var boxes = nodes.Select(Box).ToArray();

        return (
            (boxes.Min(b => b.Left) + boxes.Max(b => b.Right)) / 2,
            (boxes.Min(b => b.Top) + boxes.Max(b => b.Bottom)) / 2);
    }

    /// <summary>
    /// A box with a module nobody named in it is left alone whole. A box is drawn
    /// from where all of its modules are, so moving some of them would slide the
    /// ring off the ones that stayed.
    /// </summary>
    [Fact]
    public void A_box_only_partly_named_is_not_laid_out_at_all()
    {
        var patch = Apart(out var near, out _);

        patch.Group(near.Select(n => n.Id)).ShouldNotBeNull().Rename("Tone");

        var before = patch.Nodes.ToDictionary(n => n.Id, n => (n.X, n.Y));

        PatchLayout.Arrange(patch, NodeCatalog.BuiltIn, null, Named(near[0], near[1]));

        foreach (var node in patch.Nodes)
            (node.X, node.Y).ShouldBe(before[node.Id], "a box is laid out whole or not at all");
    }

    /// <summary>And a box named whole is, inside and out.</summary>
    [Fact]
    public void A_box_named_whole_is_laid_out_among_itself()
    {
        var patch = Apart(out var near, out var far);

        patch.Group(near.Select(n => n.Id)).ShouldNotBeNull().Rename("Tone");

        var before = patch.Nodes.ToDictionary(n => n.Id, n => (n.X, n.Y));

        PatchLayout.Arrange(patch, NodeCatalog.BuiltIn, null, Named(near));

        near[0].X.ShouldBeLessThan(near[1].X);

        foreach (var node in far)
            (node.X, node.Y).ShouldBe(before[node.Id]);
    }

    /// <summary>
    /// Naming one module is a layout with nothing to arrange, and it leaves that
    /// module where it stands rather than sliding it anywhere.
    /// </summary>
    [Fact]
    public void Laying_out_one_module_moves_it_nowhere()
    {
        var patch = Apart(out var near, out _);
        var before = patch.Nodes.ToDictionary(n => n.Id, n => (n.X, n.Y));

        PatchLayout.Arrange(patch, NodeCatalog.BuiltIn, null, Named(near[1]));

        foreach (var node in patch.Nodes)
            (node.X, node.Y).ShouldBe(before[node.Id]);
    }

    /// <summary>
    /// And laying out part of a patch twice is laying it out once, the same as
    /// the whole of one: the second press has nothing left to do.
    /// </summary>
    [Fact]
    public void Laying_out_part_of_a_patch_twice_is_laying_it_out_once()
    {
        var patch = Apart(out var near, out _);

        PatchLayout.Arrange(patch, NodeCatalog.BuiltIn, null, Named(near));

        var once = patch.Nodes.ToDictionary(n => n.Id, n => (n.X, n.Y));

        PatchLayout.Arrange(patch, NodeCatalog.BuiltIn, null, Named(near));

        foreach (var node in patch.Nodes)
            (node.X, node.Y).ShouldBe(once[node.Id]);
    }

    /// <summary>
    /// A part laid out against the edge of the canvas is slid back inside it. The
    /// coordinates clamp as they are written, so a drawing left hanging over the
    /// boundary would stack against it instead.
    /// </summary>
    [Fact]
    public void A_part_laid_out_at_the_edge_of_the_canvas_is_slid_back_inside_it()
    {
        var patch = Apart(out var near, out _);

        foreach (var node in near)
        {
            node.X = NodeInstance.Across;
            node.Y = NodeInstance.Down;
        }

        PatchLayout.Arrange(patch, NodeCatalog.BuiltIn, null, Named(near));

        near.Select(n => n.X).Distinct().Count().ShouldBe(near.Length, "they should be in columns");
        near.Max(n => n.X).ShouldBeLessThanOrEqualTo(NodeInstance.Across);
    }
}
