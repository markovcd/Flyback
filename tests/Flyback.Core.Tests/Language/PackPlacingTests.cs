using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Engine.Language;
using Flyback.Engine.Packs;
using Shouldly;

namespace Flyback.Core.Tests.Language;

/// <summary>
/// A pack's module placed in a patch: one module with its sockets in the catalog, the same
/// instrument as its body written out when compiled.
/// </summary>
public class PackPlacingTests
{
    private const string Echoes = """
        pack ada.echoes 1.0.0

        def twice(v) = v * 2

        /// A wobble, louder than asked.
        /// category: Oscillators
        module wobble(rate = 3, level = 0.25) = sine(freq: rate, amp: level)

        module doubled(in = 0) = twice(in)

        /// Two copies of what comes in, one turned over.
        module split(in = 0) = (in, in * -1)

        // Not offered: placing nothing.
        sine(freq: 1000) |> out.left
        """;

    private static ModuleCatalog Catalog(string source = Echoes)
    {
        var (catalog, issues) = PackModules.Into(NodeCatalog.BuiltIn, source);

        issues.ShouldBeEmpty();

        return catalog;
    }

    private static Patch Built(string source, ModuleCatalog catalog)
    {
        var load = PatchLanguage.Build(source, catalog);

        load.Issues.ShouldBeEmpty(load.Report);

        return load.Patch;
    }

    private static IEnumerable<(OpCode, int, int, int, int, float)> Fingerprint(CompiledPatch program) =>
        program.Ops.Select(o => (o.Code, o.Out, o.A, o.B, o.C, o.K));

    private static void SameSound(Patch actual, ModuleCatalog catalog, Patch expected) =>
        Fingerprint(actual.CompileForAudio(catalog).Program)
            .ShouldBe(Fingerprint(expected.CompileForAudio(NodeCatalog.BuiltIn).Program));

    [Fact]
    public void A_pack_module_is_one_module_in_the_catalog_with_its_sockets_and_card()
    {
        var def = Catalog().Get("ada.echoes.wobble").ShouldNotBeNull();

        def.Name.ShouldBe("Wobble");
        def.Category.ShouldBe("Oscillators");
        def.Description.ShouldBe("A wobble, louder than asked.");
        def.Inputs.Select(p => (p.Name, p.Default)).ShouldBe([("rate", 3f), ("level", 0.25f)]);
        def.Outputs.Select(p => p.Name).ShouldBe(["out"]);
    }

    [Fact]
    public void A_module_ending_with_two_things_has_two_outputs() =>
        Catalog().Get("ada.echoes.split").ShouldNotBeNull().Outputs.Select(p => p.Name).ShouldBe(["out_1", "out_2"]);

    [Fact]
    public void Placed_it_is_one_module_on_the_canvas()
    {
        var patch = Built("ada.echoes.wobble(rate: 5) |> out.left", Catalog());

        patch.Nodes.Select(n => n.TypeId).Order().ShouldBe(["ada.echoes.wobble", "output"]);
    }

    [Fact]
    public void Placed_it_sounds_as_its_body_written_out()
    {
        var catalog = Catalog();

        SameSound(
            Built("ada.echoes.wobble(rate: 5) |> out.left", catalog), catalog,
            Built("sine(freq: 5, amp: 0.25) |> out.left", NodeCatalog.BuiltIn));
    }

    [Fact]
    public void A_wire_into_a_socket_runs_to_where_the_body_reads_it()
    {
        var catalog = Catalog();

        SameSound(
            Built("ada.echoes.wobble(rate: saw(freq: 2)) |> out.left", catalog), catalog,
            Built("sine(freq: saw(freq: 2), amp: 0.25) |> out.left", NodeCatalog.BuiltIn));
    }

    [Fact]
    public void A_def_the_pack_keeps_to_itself_works_inside_its_module()
    {
        var catalog = Catalog();

        SameSound(
            Built("saw(freq: 2) |> ada.echoes.doubled() |> out.left", catalog), catalog,
            Built("saw(freq: 2) * 2 |> out.left", NodeCatalog.BuiltIn));
    }

    [Fact]
    public void Each_output_carries_its_own_part()
    {
        var catalog = Catalog();

        SameSound(
            Built("""
                let parts = saw(freq: 2) |> ada.echoes.split()
                parts.out_1 |> out.left
                parts.out_2 |> out.right
                """, catalog), catalog,
            Built("""
                let s = saw(freq: 2)
                s |> out.left
                s * -1 |> out.right
                """, NodeCatalog.BuiltIn));
    }

    [Fact]
    public void Expanding_leaves_the_patch_as_it_was()
    {
        var catalog = Catalog();
        var patch = Built("ada.echoes.wobble() |> out.left", catalog);
        var before = patch.Nodes.Select(n => n.Id).ToList();

        patch.CompileForAudio(catalog);

        patch.Nodes.Select(n => n.Id).ShouldBe(before);
    }

    [Fact]
    public void Two_compiles_name_the_inside_the_same_so_what_it_remembers_carries_over()
    {
        var catalog = Catalog();
        var patch = Built("ada.echoes.wobble() |> out.left", catalog);

        PackExpansion.Expand(patch, catalog).Nodes.Select(n => n.Id)
            .ShouldBe(PackExpansion.Expand(patch, catalog).Nodes.Select(n => n.Id));
    }

    [Fact]
    public void A_module_switched_off_hands_its_in_on()
    {
        var catalog = Catalog();
        var patch = Built("""
            let parts = saw(freq: 2) |> ada.echoes.split()
            parts.out_2 |> out.left
            off parts
            """, catalog);

        SameSound(patch, catalog, Built("saw(freq: 2) |> out.left", NodeCatalog.BuiltIn));
    }

    [Fact]
    public void A_pack_module_draws_in_the_picture_too()
    {
        var catalog = Catalog();

        Fingerprint(Built("hsv(hue: ada.echoes.wobble(rate: x)) |> out.color", catalog).CompileForVideo(catalog).Program)
            .ShouldBe(Fingerprint(Built("hsv(hue: sine(freq: x, amp: 0.25)) |> out.color", NodeCatalog.BuiltIn).CompileForVideo(NodeCatalog.BuiltIn).Program));
    }

    [Fact]
    public void Measuring_every_module_of_a_patch_holding_one_compiles_without_complaint()
    {
        var catalog = Catalog();
        var measured = Built("ada.echoes.wobble() |> out.left", catalog).CompileForMeasure(null, sound: true, catalog);

        measured.Issues.ShouldBeEmpty();
    }

    [Fact]
    public void A_pack_with_a_mistake_in_a_module_adds_nothing_and_says_where()
    {
        var (catalog, issues) = PackModules.Into(NodeCatalog.BuiltIn, """
            pack ada.broken 1.0.0
            module bad(rate = 1) = sine(nope: rate)
            """);

        catalog.Get("ada.broken.bad").ShouldBeNull();
        issues.ShouldContain(issue => issue.Line == 2 && issue.Code == IssueCode.UnknownSocket);
    }

    [Fact]
    public void A_text_that_is_not_a_pack_adds_nothing() =>
        PackModules.Into(NodeCatalog.BuiltIn, "sine() |> out.left").Issues.ShouldNotBeEmpty();

    [Fact]
    public void A_default_that_is_not_a_number_is_refused() =>
        PatchLanguage.Build("pack ada.echoes 1.0.0\nmodule tape(file = \"a.wav\") = 0", NodeCatalog.BuiltIn)
            .Issues.ShouldHaveSingleItem().Code.ShouldBe(IssueCode.ModuleDefaultNotANumber);
}
