using Flyback.Core.Graph;
using Flyback.Core.Language;
using Shouldly;

namespace Flyback.Core.Tests.Language;

/// <summary>
/// Groups in the text: a block per group, opened again where its modules do
/// not come out together, and every box a patch has written out and read back.
/// </summary>
public class GroupTextTests
{
    private static Patch Built(string source)
    {
        var load = PatchLanguage.Build(source, NodeCatalog.BuiltIn);

        load.Issues.ShouldBeEmpty(load.Report);

        return load.Patch;
    }

    private static Patch Preset(string name) =>
        Presets.All.Single(p => p.Name == name).Build(NodeCatalog.BuiltIn);

    [Fact]
    public void Blocks_with_one_name_are_one_group()
    {
        var patch = Built(
            """
            group "Voice" {
              let tone = sine(freq: 220)
            }
            let level = tone * 0.5
            group "Voice" {
              let shaped = level |> drive(drive: 2)
            }
            shaped |> out.left
            """);

        var group = patch.Groups.ShouldNotBeNull().ShouldHaveSingleItem();

        group.Name.ShouldBe("Voice");
        group.Members.Select(id => patch.Find(id)!.TypeId).Order()
            .ShouldBe(["audio.drive", "osc.sine"]);
    }

    [Fact]
    public void A_group_may_have_no_name()
    {
        var patch = Built("group {\n  let a = sine(freq: 2)\n  let b = a |> drive()\n}\nb |> out.left");

        patch.Groups.ShouldNotBeNull().ShouldHaveSingleItem().Name.ShouldBeNull();
    }

    [Fact]
    public void A_group_built_from_text_is_shut()
    {
        var patch = Built("group \"Voice\" {\n  let a = sine(freq: 2)\n  let b = a |> drive()\n}\nb |> out.left");

        patch.Groups.ShouldNotBeNull().ShouldHaveSingleItem().Collapsed.ShouldBeTrue();
    }

    [Fact]
    public void The_same_text_builds_the_same_boxes()
    {
        const string source = "group \"Voice\" {\n  let a = sine(freq: 2)\n  let b = a |> drive()\n}\nb |> out.left";

        Built(source).Groups!.Single().Id.ShouldBe(Built(source).Groups!.Single().Id);
    }

    [Fact]
    public void A_chain_stops_at_a_boxs_edge()
    {
        var patch = Built(
            """
            group "Source" {
              let a = sine(freq: 2)
              let b = a |> drive()
            }
            b |> filter(cutoff: 400) |> out.left
            """);

        var printed = PatchPrinter.Print(patch, NodeCatalog.BuiltIn);

        printed.ShouldContain("group \"Source\" {");
        printed.ShouldContain("|> filter(");

        // The Drive is said by name outside, not folded into a chain that would
        // take the Filter into the box.
        var again = Built(printed);

        again.Groups!.Single().Members.Select(id => again.Find(id)!.TypeId).Order()
            .ShouldBe(["audio.drive", "osc.sine"]);
    }

    [Fact]
    public void Whole_band_keeps_every_box_through_the_text()
    {
        var patch = Preset("Whole band");

        var printed = PatchPrinter.Print(patch, NodeCatalog.BuiltIn);
        var again = Built(printed);

        Boxes(again).ShouldBe(Boxes(patch), printed);
    }

    /// <summary>A printing of a patch with boxes is still somewhere to click.</summary>
    [Fact]
    public void A_printing_with_boxes_maps_its_modules()
    {
        var printing = PatchPrinter.Written(Preset("Whole band"), NodeCatalog.BuiltIn);

        printing.Order.ShouldNotBeEmpty();
        printing.Map.ShouldNotBeSameAs(SourceMap.Empty);
    }

    [Fact]
    public void The_text_maps_a_blocks_header_brace_and_gaps_to_its_group()
    {
        const string source = "group \"Voice\" {\n  let a = sine(freq: 2)\n\n  let b = a |> drive()\n}\nb |> out.left";

        var load = PatchLanguage.Build(source, NodeCatalog.BuiltIn);
        var voice = load.Patch.Groups.ShouldNotBeNull().ShouldHaveSingleItem().Id;

        load.Map.GroupAt(source.IndexOf("Voice", StringComparison.Ordinal)).ShouldBe(voice);
        load.Map.GroupAt(source.IndexOf("\n\n", StringComparison.Ordinal) + 1).ShouldBe(voice);
        load.Map.GroupAt(source.IndexOf('}')).ShouldBe(voice);

        // A module inside is about the module, and what is outside about no group.
        load.Map.GroupAt(source.IndexOf("sine", StringComparison.Ordinal) + 1).ShouldBeNull();
        load.Map.GroupAt(source.LastIndexOf("out", StringComparison.Ordinal)).ShouldBeNull();
    }

    [Fact]
    public void A_printing_maps_each_block_to_the_patchs_own_group()
    {
        var patch = Preset("Whole band");
        var printing = PatchPrinter.Written(patch, NodeCatalog.BuiltIn);

        var clock = patch.Groups!.Single(group => group.Name == "Clock").Id;

        printing.Map.GroupAt(printing.Source.IndexOf("group \"Clock\"", StringComparison.Ordinal)).ShouldBe(clock);
    }

    private static List<string> Boxes(Patch patch) =>
    [
        .. (patch.Groups ?? []).Select(group =>
                $"{group.Name}: " + string.Join(", ", group.Members
                    .Select(id => patch.Find(id)?.TypeId)
                    .OfType<string>()
                    .Order(StringComparer.Ordinal)))
            .Order(StringComparer.Ordinal),
    ];
}
