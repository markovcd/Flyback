using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// Groups: several modules drawn as one box, and the edge that a wire crossing
/// it puts there.
/// </summary>
/// <remarks>
/// All of this lives in the engine rather than in the canvas that draws it,
/// because none of it is drawing. Which ports a box shows is a question about
/// wires, and the answer has to be the same for a patch read off disk, one built
/// by an assistant, and one somebody is looking at.
/// <para>
/// What is <em>not</em> tested here is any effect on what a patch computes,
/// because there is none. See <see cref="NodeGroup"/>: the modules stay, the
/// wires stay, and the compiler is never told.
/// </para>
/// </remarks>
public class NodeGroupTests
{
    private static ModuleCatalog Catalog => NodeCatalog.BuiltIn;

    [Fact]
    public void A_fresh_patch_has_no_groups_at_all()
    {
        // Null rather than empty, so an ordinary patch writes no groups field.
        new Patch().Groups.ShouldBeNull();
    }

    [Fact]
    public void Grouping_draws_the_modules_together_without_moving_them()
    {
        var patch = new Patch();
        var a = Add(patch, "osc.sine", 100, 100);
        var b = Add(patch, "math.mul", 300, 100);

        patch.Connect(a.Id, 0, b.Id, 0);

        var group = patch.Group([a.Id, b.Id]).ShouldNotBeNull();

        group.Members.ShouldBe([a.Id, b.Id]);
        group.Collapsed.ShouldBeTrue();

        // The graph is exactly what it was.
        patch.Nodes.Count.ShouldBe(2);
        patch.Connections.Count.ShouldBe(1);
        (a.X, a.Y).ShouldBe((100d, 100d));
    }

    /// <summary>
    /// Where the edge comes from to begin with: the ports a wire crosses at when
    /// the box is drawn round them.
    /// </summary>
    [Fact]
    public void The_boundary_is_the_ports_where_wires_cross_it()
    {
        var patch = new Patch();

        var outside = Add(patch, "osc.sine", 0, 0);
        var inside = Add(patch, "math.mul", 200, 0);
        var alsoInside = Add(patch, "math.add", 200, 200);
        var beyond = Add(patch, "math.sub", 400, 0);

        patch.Connect(outside.Id, 0, inside.Id, 0);
        patch.Connect(inside.Id, 0, beyond.Id, 0);

        var group = patch.Group([inside.Id, alsoInside.Id]).ShouldNotBeNull();
        var sockets = patch.SocketsOf(group);

        sockets.Inputs.ShouldBe([new GroupSocket(inside.Id, 0, IsOutput: false)]);
        sockets.Outputs.ShouldBe([new GroupSocket(inside.Id, 0, IsOutput: true)]);
    }

    /// <summary>
    /// A knob is not an interface, and neither is a socket normalled to Time —
    /// nothing has ever been wired across either, so the box has nothing to show.
    /// See ADR-0050.
    /// </summary>
    [Fact]
    public void An_input_nothing_was_ever_wired_to_is_not_a_socket_on_the_box()
    {
        var patch = new Patch();

        var lone = Add(patch, "math.mul", 0, 0);
        var beside = Add(patch, "math.add", 0, 200);

        var group = patch.Group([lone.Id, beside.Id]).ShouldNotBeNull();
        var sockets = patch.SocketsOf(group);

        sockets.Inputs.ShouldBeEmpty();
        sockets.Outputs.ShouldBeEmpty();
        sockets.Rows.ShouldBe(0);
    }

    [Fact]
    public void A_wire_with_both_ends_inside_crosses_nothing()
    {
        var patch = new Patch();
        var a = Add(patch, "osc.sine", 0, 0);
        var b = Add(patch, "math.mul", 200, 0);

        patch.Connect(a.Id, 0, b.Id, 0);

        var sockets = patch.SocketsOf(patch.Group([a.Id, b.Id]).ShouldNotBeNull());

        sockets.Inputs.ShouldBeEmpty();
        sockets.Outputs.ShouldBeEmpty();
    }

    /// <summary>
    /// The two sides are not symmetric, and this is where it shows. An input
    /// takes one wire by construction; an output may feed as many as it likes,
    /// and they arrive at one socket rather than several.
    /// </summary>
    [Fact]
    public void One_inner_output_feeding_three_outsiders_is_one_socket()
    {
        var patch = new Patch();

        var source = Add(patch, "osc.sine", 0, 0);
        var beside = Add(patch, "math.add", 0, 200);

        for (var i = 0; i < 3; i++)
            patch.Connect(source.Id, 0, Add(patch, "math.mul", 300, i * 100).Id, 0);

        var sockets = patch.SocketsOf(patch.Group([source.Id, beside.Id]).ShouldNotBeNull());

        sockets.Outputs.ShouldBe([new GroupSocket(source.Id, 0, IsOutput: true)]);
    }

    /// <summary>
    /// The edge is a thing you arrange, not a thing that happens to you. Taking a
    /// wire off leaves the socket it was on, so the box does not shrink under the
    /// hand that has just unplugged something and there is somewhere to plug it
    /// back into.
    /// </summary>
    [Fact]
    public void Unplugging_a_wire_leaves_the_socket_behind()
    {
        var patch = new Patch();

        var feed = Add(patch, "osc.sine", 0, 0);
        var inside = Add(patch, "math.mul", 200, 0);
        var beside = Add(patch, "math.add", 200, 200);

        patch.Connect(feed.Id, 0, inside.Id, 0);

        var group = patch.Group([inside.Id, beside.Id]).ShouldNotBeNull();
        var socket = new GroupSocket(inside.Id, 0, IsOutput: false);

        patch.SocketsOf(group).Inputs.ShouldBe([socket]);
        patch.Wired(group, socket).ShouldBeTrue();

        patch.Disconnect(inside.Id, 0);

        patch.SocketsOf(group).Inputs.ShouldBe([socket]);
        patch.Wired(group, socket).ShouldBeFalse();
    }

    /// <summary>
    /// And a wire drawn across the edge after the box was made puts a socket
    /// there on the same terms — otherwise the edge would be whatever it happened
    /// to be at the moment of grouping and could never grow.
    /// </summary>
    [Fact]
    public void A_wire_drawn_across_the_edge_later_puts_a_socket_there_too()
    {
        var patch = new Patch();

        var feed = Add(patch, "osc.sine", 0, 0);
        var a = Add(patch, "math.mul", 200, 0);
        var b = Add(patch, "math.add", 200, 200);

        var group = patch.Group([a.Id, b.Id]).ShouldNotBeNull();
        patch.SocketsOf(group).Rows.ShouldBe(0);

        patch.Connect(feed.Id, 0, b.Id, 1);

        var socket = new GroupSocket(b.Id, 1, IsOutput: false);
        patch.SocketsOf(group).Inputs.ShouldBe([socket]);

        patch.Disconnect(b.Id, 1);
        patch.SocketsOf(group).Inputs.ShouldBe([socket]);
    }

    /// <summary>
    /// A wire drawn between two modules that are both inside crosses nothing, so
    /// it puts no socket anywhere — the same rule as before, applied to the list
    /// rather than only to the drawing.
    /// </summary>
    [Fact]
    public void A_wire_drawn_inside_the_box_puts_no_socket_on_it()
    {
        var patch = new Patch();

        var a = Add(patch, "osc.sine", 0, 0);
        var b = Add(patch, "math.mul", 200, 0);

        var group = patch.Group([a.Id, b.Id]).ShouldNotBeNull();

        patch.Connect(a.Id, 0, b.Id, 0);

        group.Exposed.ShouldBeEmpty();
        patch.SocketsOf(group).Rows.ShouldBe(0);
    }

    [Fact]
    public void A_socket_can_be_taken_off_the_edge_once_nothing_is_on_it()
    {
        var patch = new Patch();

        var feed = Add(patch, "osc.sine", 0, 0);
        var inside = Add(patch, "math.mul", 200, 0);
        var beside = Add(patch, "math.add", 200, 200);

        patch.Connect(feed.Id, 0, inside.Id, 0);

        var group = patch.Group([inside.Id, beside.Id]).ShouldNotBeNull();
        var socket = new GroupSocket(inside.Id, 0, IsOutput: false);

        patch.Disconnect(inside.Id, 0);
        group.Hide(socket).ShouldBeTrue();

        patch.SocketsOf(group).Inputs.ShouldBeEmpty();
    }

    /// <summary>
    /// Hiding one that is still wired is not refused so much as futile: a
    /// crossing wire is a socket whatever the stored list says, so it comes
    /// straight back. Pinned because the editor declines the gesture on exactly
    /// this ground.
    /// </summary>
    [Fact]
    public void A_socket_with_a_wire_on_it_comes_back_however_it_is_hidden()
    {
        var patch = new Patch();

        var feed = Add(patch, "osc.sine", 0, 0);
        var inside = Add(patch, "math.mul", 200, 0);
        var beside = Add(patch, "math.add", 200, 200);

        patch.Connect(feed.Id, 0, inside.Id, 0);

        var group = patch.Group([inside.Id, beside.Id]).ShouldNotBeNull();
        var socket = new GroupSocket(inside.Id, 0, IsOutput: false);

        group.Hide(socket);

        patch.Wired(group, socket).ShouldBeTrue();
        patch.SocketsOf(group).Inputs.ShouldBe([socket]);
    }

    /// <summary>
    /// An input and an output are different sockets even at the same port
    /// number, which most modules have: a Multiply has both an 'a' at 0 and an
    /// 'out' at 0.
    /// </summary>
    [Fact]
    public void The_two_sides_of_one_port_number_are_two_sockets()
    {
        var patch = new Patch();

        var feed = Add(patch, "osc.sine", 0, 0);
        var inside = Add(patch, "math.mul", 200, 0);
        var beside = Add(patch, "math.add", 200, 200);
        var beyond = Add(patch, "math.sub", 400, 0);

        patch.Connect(feed.Id, 0, inside.Id, 0);
        patch.Connect(inside.Id, 0, beyond.Id, 0);

        var group = patch.Group([inside.Id, beside.Id]).ShouldNotBeNull();
        var sockets = patch.SocketsOf(group);

        sockets.Inputs.ShouldBe([new GroupSocket(inside.Id, 0, IsOutput: false)]);
        sockets.Outputs.ShouldBe([new GroupSocket(inside.Id, 0, IsOutput: true)]);

        // Unplugging one leaves the other alone, which it could not if the two
        // were the same entry.
        patch.Disconnect(inside.Id, 0);

        patch.Wired(group, new GroupSocket(inside.Id, 0, IsOutput: false)).ShouldBeFalse();
        patch.Wired(group, new GroupSocket(inside.Id, 0, IsOutput: true)).ShouldBeTrue();
    }

    [Fact]
    public void Sockets_are_ordered_down_the_canvas()
    {
        var patch = new Patch();

        var lower = Add(patch, "math.mul", 0, 400);
        var upper = Add(patch, "math.add", 0, 100);
        var feed = Add(patch, "osc.sine", -300, 0);

        patch.Connect(feed.Id, 0, lower.Id, 0);
        patch.Connect(feed.Id, 0, upper.Id, 0);

        var sockets = patch.SocketsOf(patch.Group([lower.Id, upper.Id]).ShouldNotBeNull());

        // Wired lower-first, drawn upper-first: the order is where the modules
        // sit rather than what order the wires were made in.
        sockets.Inputs.ShouldBe(
            [new GroupSocket(upper.Id, 0, IsOutput: false), new GroupSocket(lower.Id, 0, IsOutput: false)]);
    }

    /// <summary>
    /// A patch has exactly one Output and it is not a thing that can be put in a
    /// box. Left out rather than refused, the way copying leaves it out — Ctrl+A
    /// then group should group everything that can be, not nothing.
    /// </summary>
    [Fact]
    public void The_output_is_left_out_rather_than_refusing_the_group()
    {
        var patch = new Patch();
        var sink = patch.EnsureOutput(Catalog);
        var sine = Add(patch, "osc.sine", 0, 0);
        var mul = Add(patch, "math.mul", 200, 0);

        var group = patch.Group([sine.Id, mul.Id, sink.Id]).ShouldNotBeNull();

        group.Members.ShouldBe([sine.Id, mul.Id]);
    }

    [Fact]
    public void Grouping_nothing_that_can_be_grouped_makes_no_group()
    {
        var patch = new Patch();
        var sink = patch.EnsureOutput(Catalog);

        patch.Group([sink.Id]).ShouldBeNull();
        patch.Groups.ShouldBeNull();
    }

    /// <summary>
    /// A box round one module is the module again with a second name: every
    /// socket it could show is one that module already has, in the same order.
    /// Refused rather than drawn, and refused here rather than in the gesture, so
    /// it is refused however it is asked for.
    /// </summary>
    [Fact]
    public void One_module_is_not_a_group()
    {
        var patch = new Patch();
        var sine = Add(patch, "osc.sine", 0, 0);

        patch.Group([sine.Id]).ShouldBeNull();
        patch.Groups.ShouldBeNull();

        // Including when the rest of what was asked for cannot be grouped.
        var sink = patch.EnsureOutput(Catalog);

        patch.Group([sine.Id, sink.Id]).ShouldBeNull();
        patch.Groups.ShouldBeNull();

        // And the same module asked for twice is still one module.
        patch.Group([sine.Id, sine.Id]).ShouldBeNull();
        patch.Groups.ShouldBeNull();
    }

    /// <summary>
    /// Two boxes both claiming to draw one module is a picture that means
    /// nothing, so a module joining a group leaves the one it was in.
    /// </summary>
    [Fact]
    public void A_module_belongs_to_one_group_at_a_time()
    {
        var patch = new Patch();
        var a = Add(patch, "osc.sine", 0, 0);
        var b = Add(patch, "math.mul", 200, 0);
        var c = Add(patch, "math.add", 400, 0);
        var d = Add(patch, "math.sub", 600, 0);

        var first = patch.Group([a.Id, b.Id, c.Id]).ShouldNotBeNull();
        var second = patch.Group([c.Id, d.Id]).ShouldNotBeNull();

        first.Members.ShouldBe([a.Id, b.Id]);
        second.Members.ShouldBe([c.Id, d.Id]);
        patch.GroupOf(c.Id).ShouldBe(second);
    }

    /// <summary>
    /// The rule against a box round one module holds after an edit and not only
    /// when one is made, so a group robbed down to a single member stops being
    /// one rather than becoming the picture that was refused.
    /// </summary>
    [Fact]
    public void A_group_worn_down_to_one_module_stops_existing()
    {
        var patch = new Patch();
        var a = Add(patch, "osc.sine", 0, 0);
        var b = Add(patch, "math.mul", 200, 0);
        var c = Add(patch, "math.add", 400, 0);
        var d = Add(patch, "math.sub", 600, 0);

        var first = patch.Group([a.Id, b.Id]).ShouldNotBeNull();

        // Taking b into another group leaves a on its own, which is not a group.
        patch.Group([b.Id, c.Id, d.Id]).ShouldNotBeNull();

        patch.Groups.ShouldNotBeNull().ShouldNotContain(first);
        patch.Groups.Count.ShouldBe(1);
        patch.GroupOf(a.Id).ShouldBeNull();
    }

    /// <summary>
    /// A box may not go on claiming to draw a module that is not there, and it
    /// may not shrink to one either — so deleting takes the group as soon as
    /// there are too few left to be one.
    /// </summary>
    [Fact]
    public void Deleting_the_modules_takes_the_group_with_them()
    {
        var patch = new Patch();
        var a = Add(patch, "osc.sine", 0, 0);
        var b = Add(patch, "math.mul", 200, 0);
        var c = Add(patch, "math.add", 400, 0);

        patch.Group([a.Id, b.Id, c.Id]);

        patch.Remove(a.Id);
        patch.Groups.ShouldNotBeNull().Single().Members.ShouldBe([b.Id, c.Id]);

        // One more leaves a single module, which is not a group.
        patch.Remove(b.Id);
        patch.Groups.ShouldBeNull();
    }

    [Fact]
    public void Ungrouping_leaves_every_module_and_every_wire_alone()
    {
        var patch = new Patch();
        var a = Add(patch, "osc.sine", 100, 100);
        var b = Add(patch, "math.mul", 300, 100);

        patch.Connect(a.Id, 0, b.Id, 0);

        var group = patch.Group([a.Id, b.Id]).ShouldNotBeNull();
        patch.Ungroup(group.Id).ShouldBeTrue();

        patch.Groups.ShouldBeNull();
        patch.Nodes.Count.ShouldBe(2);
        patch.Connections.Count.ShouldBe(1);
        (a.X, a.Y).ShouldBe((100d, 100d));
    }

    [Fact]
    public void Only_a_collapsed_group_stands_in_front_of_its_modules()
    {
        var patch = new Patch();
        var sine = Add(patch, "osc.sine", 0, 0);
        var mul = Add(patch, "math.mul", 200, 0);

        var group = patch.Group([sine.Id, mul.Id]).ShouldNotBeNull();
        patch.CollapsedGroupOf(sine.Id).ShouldBe(group);

        group.Collapsed = false;
        patch.CollapsedGroupOf(sine.Id).ShouldBeNull();
        patch.GroupOf(sine.Id).ShouldBe(group);
    }

    /// <summary>
    /// Groups go through a file, which is also how undo and redo carry them —
    /// history snapshots a patch by writing it (see PatchHistory).
    /// </summary>
    [Fact]
    public void A_group_survives_being_written_and_read_back()
    {
        var patch = new Patch();
        patch.EnsureOutput(Catalog);

        var feed = Add(patch, "time", -200, 0);
        var a = Add(patch, "osc.sine", 0, 0);
        var b = Add(patch, "math.mul", 200, 0);

        patch.Connect(feed.Id, 0, a.Id, 1);

        var group = patch.Group([a.Id, b.Id]).ShouldNotBeNull();
        group.Name = "Voice";
        group.Collapsed = false;

        // Unplugged before it is written, so the socket is on the edge with
        // nothing holding it there but the list itself — which is the case a
        // round trip has to carry and the only one that would notice if it did not.
        patch.Disconnect(a.Id, 1);
        group.Exposed.ShouldBe([new GroupSocket(a.Id, 1, IsOutput: false)]);

        var read = PatchIo.Read(PatchIo.ToJson(patch, Catalog), Catalog).Patch;
        var back = read.Groups.ShouldNotBeNull().Single();

        back.Id.ShouldBe(group.Id);
        back.Name.ShouldBe("Voice");
        back.Collapsed.ShouldBeFalse();
        back.Members.ShouldBe([a.Id, b.Id]);

        // The edge too, which is the half of it a wire cannot put back: a socket
        // nothing is plugged into exists only because it was written down.
        back.Exposed.ShouldBe(group.Exposed);
    }

    /// <summary>
    /// The reason this could be added to the format without moving the version:
    /// a file written before groups existed reads exactly as it always did.
    /// </summary>
    [Fact]
    public void A_patch_with_no_groups_writes_no_groups_field()
    {
        var patch = new Patch();
        patch.EnsureOutput(Catalog);

        PatchIo.ToJson(patch, Catalog).ShouldNotContain("Groups");
    }

    [Fact]
    public void A_group_names_itself_after_how_many_are_in_it_until_it_is_named()
    {
        var patch = new Patch();

        var a = Add(patch, "osc.sine", 0, 0);
        var b = Add(patch, "math.mul", 200, 0);

        var group = patch.Group([a.Id, b.Id]).ShouldNotBeNull();
        group.Title().ShouldBe("2 modules");

        group.Members.Add(Add(patch, "math.add", 400, 0).Id);
        group.Title().ShouldBe("3 modules");

        group.Rename("Voice");
        group.Title().ShouldBe("Voice");

        // Emptied, it goes back to counting itself rather than keeping a blank.
        group.Rename("   ");
        group.Name.ShouldBeNull();
        group.Title().ShouldBe("3 modules");
    }

    private static NodeInstance Add(Patch patch, string typeId, double x, double y)
    {
        var node = NodeInstance.Create(Catalog.Require(typeId), x, y);

        patch.Nodes.Add(node);
        return node;
    }
}
