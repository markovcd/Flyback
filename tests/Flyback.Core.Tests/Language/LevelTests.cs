using Flyback.Core.Graph;
using Flyback.Engine.Language;
using Shouldly;

namespace Flyback.Core.Tests.Language;

/// <summary>The groups, def calls, loops and passes the text opens, inside one another, and the modules each placed.</summary>
public class LevelTests
{
    private static LanguageLoad Built(string source)
    {
        var load = PatchLanguage.Build(source, NodeCatalog.BuiltIn);

        load.Issues.ShouldBeEmpty(load.Report);

        return load;
    }

    private static string[] Types(LanguageLoad load, IEnumerable<Guid> ids) =>
        [.. ids.Select(id => load.Patch.Find(id)!.TypeId).Order()];

    [Fact]
    public void A_patch_with_no_blocks_opens_no_levels() =>
        Built("sine(freq: 220) |> out.left").Levels.ShouldBeEmpty();

    [Fact]
    public void A_group_inside_a_group_is_a_level_inside_it_holding_its_own_modules()
    {
        var load = Built(
            """
            group "A" {
              let a = sine(freq: 2) |> drive(drive: 2)
              group "B" {
                let b = saw(freq: 3) |> drive(drive: 2)
              }
            }
            a + b |> out.left
            """);

        var outer = load.Levels.ShouldHaveSingleItem();
        outer.Kind.ShouldBe(LevelKind.Group);
        outer.Label.ShouldBe("A");
        outer.Line.ShouldBe(1);
        Types(load, outer.Members).ShouldBe(["audio.drive", "osc.sine"]);

        var inner = outer.Inside.ShouldHaveSingleItem();
        inner.Label.ShouldBe("B");
        inner.Line.ShouldBe(3);
        Types(load, inner.Members).ShouldBe(["audio.drive", "osc.saw"]);
        Types(load, outer.All).ShouldBe(["audio.drive", "audio.drive", "osc.saw", "osc.sine"]);
    }

    [Fact]
    public void A_call_is_a_level_named_for_its_def_and_the_let_that_holds_it()
    {
        var load = Built(
            """
            def voice(freq) = {
              def shaped(in) = in |> drive(drive: 2)
              sine(freq: freq) |> shaped()
            }
            let bass = voice(55)
            bass |> out.left
            """);

        var call = load.Levels.ShouldHaveSingleItem();
        call.Kind.ShouldBe(LevelKind.Call);
        call.Name.ShouldBe("voice");
        call.Binding.ShouldBe("bass");
        call.Label.ShouldBe("bass = voice");
        Types(load, call.Members).ShouldBe(["osc.sine"]);

        var shaped = call.Inside.ShouldHaveSingleItem();
        shaped.Label.ShouldBe("shaped");
        Types(load, shaped.Members).ShouldBe(["audio.drive"]);
    }

    [Fact]
    public void A_loop_holds_a_pass_for_each_item_numbered_from_one()
    {
        var load = Built(
            """
            let pads = for pitch in [220, 330] {
              saw(freq: pitch) |> drive(drive: 3)
            }
            sum(pads) |> out.left
            """);

        var loop = load.Levels.ShouldHaveSingleItem();
        loop.Kind.ShouldBe(LevelKind.Loop);
        loop.Label.ShouldBe("pads = for pitch ×2");
        loop.Members.ShouldBeEmpty();

        loop.Inside.Select(pass => (pass.Kind, pass.Label, pass.Item)).ShouldBe(
            [(LevelKind.Pass, "pass 1/2", "220"), (LevelKind.Pass, "pass 2/2", "330")]);
        loop.Inside.ShouldAllBe(pass => pass.Members.Count == 2);
    }

    [Fact]
    public void A_group_in_a_loop_is_a_level_in_each_pass_with_its_numbered_name()
    {
        var load = Built(
            """
            let v = for k in 1..2 {
              group "Pad" {
                let s = sine(freq: k) |> drive(drive: 2)
              }
              s
            }
            sum(v) |> out.left
            """);

        load.Levels.ShouldHaveSingleItem().Inside
            .Select(pass => pass.Inside.ShouldHaveSingleItem().Label)
            .ShouldBe(["Pad 1", "Pad 2"]);
    }

    [Fact]
    public void Every_module_a_level_holds_is_in_the_patch_and_in_one_level_only()
    {
        var load = Built(
            """
            def tone(f) = sine(freq: f) * 0.5 + 0.1
            group "G" {
              let a = tone(110) |> drive(drive: 2)
            }
            let v = for k in 1..2 { tone(k * 220) }
            a + sum(v) |> out.left
            """);

        var held = load.Levels.SelectMany(level => level.All).ToList();

        held.ShouldBeUnique();
        held.ShouldAllBe(id => load.Patch.Find(id) != null);
    }

    [Fact]
    public void The_path_to_a_module_runs_from_the_outermost_level_down()
    {
        var load = Built(
            """
            let v = for k in 1..2 {
              group "Pad" {
                let s = saw(freq: k) |> drive(drive: 2)
              }
              s
            }
            sum(v) + sine(freq: 5) |> out.left
            """);

        var saw = load.Patch.Nodes.First(n => n.TypeId == "osc.saw");

        Level.PathTo(load.Levels, saw.Id).ShouldNotBeNull().Select(level => level.Label)
            .ShouldBe(["v = for k ×2", "pass 1/2", "Pad 1"]);

        var outside = load.Patch.Nodes.First(n => n.TypeId == "osc.sine");
        Level.PathTo(load.Levels, outside.Id).ShouldBeNull();
    }
}
