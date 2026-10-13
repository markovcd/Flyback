using Flyback.Core.Graph;
using Flyback.Engine.Language;
using Shouldly;

namespace Flyback.Core.Tests.Language;

/// <summary>
/// A def call is drawn as a shut box round the modules it stamped out: the
/// outermost call only, and never inside a group block.
/// </summary>
public class DefBoxTests
{
    private const string Voice =
        """
        def voice(freq) = sine(freq: freq) |> drive(drive: 2)

        """;

    private static Patch Built(string source)
    {
        var load = PatchLanguage.Build(source, NodeCatalog.BuiltIn);

        load.Issues.ShouldBeEmpty(load.Report);

        return load.Patch;
    }

    private static string[] Types(Patch patch, NodeGroup group) =>
        [.. group.Members.Select(id => patch.Find(id)!.TypeId).Order()];

    [Fact]
    public void Each_call_is_its_own_shut_box_named_after_the_def()
    {
        var patch = Built(Voice + "voice(220) |> out.left\nvoice(330) |> out.right");

        var groups = patch.Groups.ShouldNotBeNull();
        groups.Count.ShouldBe(2);
        groups.ShouldAllBe(g => g.Collapsed && g.Name == "voice");
        groups[0].Members.Intersect(groups[1].Members).ShouldBeEmpty();
        groups.ShouldAllBe(g => Types(patch, g).SequenceEqual(new[] { "audio.drive", "osc.sine" }));
    }

    [Fact]
    public void A_let_names_the_box_of_the_call_it_binds()
    {
        var patch = Built(Voice + "let bass = voice(110)\nbass |> out.left");

        patch.Groups.ShouldNotBeNull().ShouldHaveSingleItem().Name.ShouldBe("bass");
    }

    [Fact]
    public void A_let_that_binds_more_than_the_call_leaves_the_def_name()
    {
        var patch = Built(Voice + "let loud = voice(110) |> drive(drive: 4)\nloud |> out.left");

        var group = patch.Groups.ShouldNotBeNull().ShouldHaveSingleItem();
        group.Name.ShouldBe("voice");
        group.Members.Count.ShouldBe(2);
    }

    [Fact]
    public void A_call_inside_a_def_is_in_the_outer_call_box()
    {
        var patch = Built(
            Voice
            + """
              def pair(freq) = voice(freq) |> drive(drive: 3)
              pair(220) |> out.left
              """);

        var group = patch.Groups.ShouldNotBeNull().ShouldHaveSingleItem();
        group.Name.ShouldBe("pair");
        Types(patch, group).ShouldBe(["audio.drive", "audio.drive", "osc.sine"]);
    }

    [Fact]
    public void A_call_inside_a_group_block_is_in_the_block_box()
    {
        var patch = Built(Voice + "group \"Bass\" {\n  let bass = voice(110)\n}\nbass |> out.left");

        patch.Groups.ShouldNotBeNull().ShouldHaveSingleItem().Name.ShouldBe("Bass");
    }

    [Fact]
    public void A_def_that_places_one_module_draws_no_box_and_says_nothing()
    {
        var patch = Built("def tone(freq) = sine(freq: freq)\ntone(220) |> out.left");

        (patch.Groups?.Count ?? 0).ShouldBe(0);
    }

    [Fact]
    public void What_the_caller_passes_in_stays_outside_the_box()
    {
        var patch = Built(
            """
            def shaped(in) = in |> drive(drive: 2) |> drive(drive: 3)
            sine(freq: 220) |> shaped() |> out.left
            """);

        var group = patch.Groups.ShouldNotBeNull().ShouldHaveSingleItem();
        Types(patch, group).ShouldBe(["audio.drive", "audio.drive"]);
    }

    [Fact]
    public void The_same_text_builds_the_same_boxes()
    {
        var source = Voice + "voice(220) |> out.left\nvoice(330) |> out.right";

        Built(source).Groups!.Select(g => g.Id).ShouldBe(Built(source).Groups!.Select(g => g.Id));
    }

    [Fact]
    public void A_printing_keeps_each_call_box()
    {
        var patch = Built(Voice + "voice(220) |> out.left\nvoice(330) |> out.right");

        var again = Built(PatchPrinter.Print(patch, NodeCatalog.BuiltIn));

        again.Groups.ShouldNotBeNull().Count.ShouldBe(2);
    }
}
