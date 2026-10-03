using Flyback.Cli.Common;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Shouldly;
using Xunit;

namespace Flyback.Cli.Tests;

/// <summary>What <c>render --mute</c> and <c>--solo</c> switch off, by group name.</summary>
public class GroupSwitchesTests
{
    private static Patch Band() => Presets.All.Single(p => p.Name == "Whole band").Build(NodeCatalog.BuiltIn);

    private static HashSet<Guid> Members(Patch patch, string group) =>
        [.. patch.Groups!.Single(g => g.Name == group).Members];

    [Fact]
    public void A_muted_group_has_its_modules_switched_off_and_nothing_else()
    {
        var patch = Band();
        var kick = Members(patch, "Kick");

        GroupSwitches.Apply(patch, ["kick"], [], new StringWriter()).ShouldBeTrue();

        patch.Nodes.Where(n => n.Off).Select(n => n.Id).ShouldBe(kick, ignoreOrder: true);
    }

    [Fact]
    public void A_solo_keeps_the_group_and_what_feeds_it_and_never_the_output_off()
    {
        var patch = Band();
        var bass = Members(patch, "Bass");

        GroupSwitches.Apply(patch, [], ["Bass"], new StringWriter()).ShouldBeTrue();

        patch.Nodes.Where(n => bass.Contains(n.Id)).ShouldAllBe(n => !n.Off);
        patch.Nodes.Where(n => NodeCatalog.IsSink(n.TypeId)).ShouldAllBe(n => !n.Off);
        // The kick ducks the bass, so some of it feeds the bass and stays; the rest of it goes.
        patch.Nodes.Where(n => Members(patch, "Kick").Contains(n.Id)).ShouldContain(n => n.Off);
        patch.Nodes.Where(n => Members(patch, "Hats").Contains(n.Id)).ShouldContain(n => n.Off);
    }

    [Fact]
    public void A_name_that_is_no_groups_is_refused_with_the_names_there_are()
    {
        var patch = Band();
        var error = new StringWriter();

        GroupSwitches.Apply(patch, ["Nonesuch"], [], error).ShouldBeFalse();

        error.ToString().ShouldContain("no group is called 'Nonesuch'");
        error.ToString().ShouldContain("Kick");
        patch.Nodes.ShouldAllBe(n => !n.Off);
    }
}
