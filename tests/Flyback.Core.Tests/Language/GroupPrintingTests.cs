using Flyback.Core.Graph;
using Flyback.Core.Language;
using Shouldly;

namespace Flyback.Core.Tests.Language;

/// <summary>
/// The boxes, which the printer used to drop.
/// </summary>
/// <remarks>
/// A group is presentation — the compiler is never told about one — but the
/// largest preset uses ten of them as its organising device, and a text form
/// that dropped them was unreadable at that size. The reason it dropped them was
/// real: a binding is written where something first needs it, and that order
/// scatters a group's members, so a block could not simply be opened around a
/// run of them. The order is worked out again with each group counted as one
/// thing, which is what these hold it to.
/// </remarks>
public class GroupPrintingTests
{
    private static Patch Preset(string name) =>
        Presets.All.Single(preset => preset.Name == name).Build(NodeCatalog.BuiltIn);

    private static string Print(Patch patch) => PatchPrinter.Print(patch, NodeCatalog.BuiltIn);

    private static Patch Back(Patch patch)
    {
        var load = PatchLanguage.Build(Print(patch), NodeCatalog.BuiltIn);

        load.Issues.ShouldBeEmpty(load.Report);

        return load.Patch;
    }

    /// <summary>
    /// What each box is called and how many modules are in it, by name.
    /// </summary>
    /// <remarks>
    /// Sorted, because the order the boxes come out in is the order the
    /// statements were written and that is exactly what this pass reworks. What
    /// has to survive is which modules are in which box, not which box was
    /// written first.
    /// </remarks>
    private static string[] Boxes(Patch patch) =>
        [.. (patch.Groups ?? [])
            .GroupBy(group => group.Name ?? string.Empty)
            .Select(named => $"{named.Key}: {named.Sum(group => group.Members.Count)}")
            .Order(StringComparer.Ordinal)];

    public static TheoryData<string> Grouped =>
        [.. Presets.All
            .Select(preset => preset.Name)
            .Where(name => Preset(name).Groups is { Count: > 0 })];

    /// <summary>
    /// The one that matters, over every preset that has a box in it. Ten of the
    /// largest preset's, with every module in the one it was in.
    /// </summary>
    [Theory]
    [MemberData(nameof(Grouped))]
    public void Every_box_survives_being_written_out_and_read_back(string name)
    {
        var patch = Preset(name);

        Boxes(Back(patch)).ShouldBe(Boxes(patch));
    }

    [Fact]
    public void The_largest_preset_really_does_have_ten_of_them()
    {
        // So the theory above is not passing over a patch with none in it.
        Preset("Whole band").Groups.ShouldNotBeNull().Count.ShouldBe(10);
    }

    /// <summary>
    /// A block is written as one, whatever order its members were settled in.
    /// Whole band's clock is four modules that half the patch reaches for, so
    /// emit-on-first-use spreads them from one end of it to the other.
    /// </summary>
    [Fact]
    public void A_box_is_written_as_one_run_of_statements()
    {
        var lines = Print(Preset("Whole band")).ReplaceLineEndings("\n").Split('\n');

        var opened = 0;
        var seen = new List<string>();

        foreach (var line in lines)
        {
            if (line.StartsWith("group ", StringComparison.Ordinal))
            {
                opened++;
                seen.Add(line);
                continue;
            }

            if (line == "}") opened--;

            opened.ShouldBeInRange(0, 1, "a box is never opened inside another");
        }

        opened.ShouldBe(0, "every box that was opened is closed");
        seen.Count.ShouldBe(10);
    }

    /// <summary>
    /// A module folded into the middle of a pipeline is declared wherever that
    /// pipeline is, so anything in a box is given a name of its own whatever its
    /// shape — otherwise it would land outside the block.
    /// </summary>
    [Fact]
    public void A_module_in_a_box_is_always_given_a_name()
    {
        var patch = Preset("Whole band");
        var printed = Print(patch).ReplaceLineEndings("\n").Split('\n');

        var declared = printed.Count(line => line.TrimStart().StartsWith("let ", StringComparison.Ordinal));
        var boxed = (patch.Groups ?? []).Sum(group => group.Members.Count);

        declared.ShouldBeGreaterThanOrEqualTo(boxed);
    }

    /// <summary>
    /// The clock is one module however many lines say `t`, so whichever block
    /// happens to mention it first must not take it. A patch whose clock is in
    /// nobody's box comes back with it in nobody's box.
    /// </summary>
    [Fact]
    public void A_block_that_mentions_the_clock_does_not_adopt_it()
    {
        var load = PatchLanguage.Build(
            """
            group "Motion" {
              let slowly = t * 0.2
              let turn = rotate(angle: slowly)
            }

            turn |> out.color
            """,
            NodeCatalog.BuiltIn);

        load.Issues.ShouldBeEmpty(load.Report);

        var clock = load.Patch.Nodes.Single(node => node.TypeId == NodeCatalog.TimeTypeId);
        var box = load.Patch.Groups.ShouldNotBeNull().ShouldHaveSingleItem();

        box.Members.ShouldNotContain(clock.Id);
        box.Members.Count.ShouldBe(2);
    }

    /// <summary>
    /// And where somebody has drawn a box round the clock, it is written as the
    /// module it is rather than as the bare word — a bare word is a reading
    /// rather than a declaration, and a module declared nowhere can be in
    /// nobody's group.
    /// </summary>
    [Fact]
    public void A_clock_in_a_box_is_written_as_a_module()
    {
        var patch = Preset("Whole band");
        var clock = patch.Nodes.Single(node => node.TypeId == NodeCatalog.TimeTypeId);

        (patch.Groups ?? []).ShouldContain(group => group.Members.Contains(clock.Id));

        Print(patch).ShouldContain("= time()");
    }

    /// <summary>
    /// A box keeps its identity across a rebuild the way a module does, so the
    /// canvas does not forget which one was open on every evaluation.
    /// </summary>
    [Fact]
    public void A_box_is_the_same_box_every_time_it_is_built()
    {
        var printed = Print(Preset("Whole band"));

        var first = PatchLanguage.Build(printed, NodeCatalog.BuiltIn).Patch.Groups;
        var second = PatchLanguage.Build(printed, NodeCatalog.BuiltIn).Patch.Groups;

        first.ShouldNotBeNull().Select(group => group.Id)
            .ShouldBe(second.ShouldNotBeNull().Select(group => group.Id));
    }

    /// <summary>
    /// Two boxes reaching into each other cannot both be written as a run, and
    /// what gives way is a box rather than the patch. Presentation is worth
    /// less than an order that reads.
    /// </summary>
    [Fact]
    public void Two_boxes_that_reach_into_each_other_cost_a_box_and_not_the_patch()
    {
        var builder = new PatchBuilder();
        var time = builder.Add("time", 0, 0);
        var one = builder.Add("osc.sine", 0, 0, (1, 2f));
        var two = builder.Add("math.mul", 0, 0);
        var three = builder.Add("osc.saw", 0, 0);
        var four = builder.Add("math.add", 0, 0);
        var sink = builder.Add(NodeCatalog.OutputTypeId, 0, 0, (NodeCatalog.OutputGainPort, 1f));

        builder
            .Wire(time, 0, one, 0)
            .Wire(one, 0, two, 0)
            .Wire(two, 0, three, 0)
            .Wire(three, 0, four, 0)
            .Wire(four, 0, sink, NodeCatalog.OutputColorPort);

        // One box holding the first and third stages, another holding the second
        // and fourth: each reaches through the other, so no order stands them
        // both together.
        builder.Patch.Group([one.Id, three.Id]);
        builder.Patch.Group([two.Id, four.Id]);

        var load = PatchLanguage.Build(Print(builder.Patch), NodeCatalog.BuiltIn);

        load.Issues.ShouldBeEmpty(load.Report);
        load.Patch.Nodes.Count.ShouldBe(builder.Patch.Nodes.Count);
        (load.Patch.Groups?.Count ?? 0).ShouldBeLessThan(2);
    }
}
