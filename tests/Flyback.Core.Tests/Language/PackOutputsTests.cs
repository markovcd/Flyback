using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Engine.Compile;
using Flyback.Engine.Language;
using Flyback.Engine.Packs;
using Shouldly;

namespace Flyback.Core.Tests.Language;

/// <summary>A pack module's <c>out</c> lines: each output named, with its help.</summary>
public class PackOutputsTests
{
    private const string Split = """
        pack ada.split 1.0.0

        module split(rate = 2) = {
          let low = sine(freq: rate)
          out low "The slow one."
          out high = saw(freq: rate) "The bright one."
        }
        """;

    private static ModuleCatalog Catalog(string source = Split)
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

    private static IEnumerable<(OpCode, int, int, int, int, float)> Fingerprint(Patch patch, ModuleCatalog catalog) =>
        patch.CompileForAudio(catalog).Program.Ops.Select(o => (o.Code, o.Out, o.A, o.B, o.C, o.K));

    private static IReadOnlyList<LanguageIssue> Read(string module) =>
        PackModules.Into(NodeCatalog.BuiltIn, "pack ada.bad 1.0.0\n" + module).Issues;

    [Fact]
    public void Each_out_line_names_an_output_and_says_what_it_gives()
    {
        var def = Catalog().Get("ada.split.split").ShouldNotBeNull();

        def.Outputs.Select(p => (p.Name, p.Help)).ShouldBe([("low", "The slow one."), ("high", "The bright one.")]);
    }

    [Fact]
    public void A_wire_takes_a_named_output_by_its_name()
    {
        var catalog = Catalog();

        Fingerprint(Built("ada.split.split(rate: 3).high |> out.left", catalog), catalog)
            .ShouldBe(Fingerprint(Built("saw(freq: 3) |> out.left", NodeCatalog.BuiltIn), NodeCatalog.BuiltIn));
    }

    [Fact]
    public void One_out_line_is_one_output_by_that_name()
    {
        var catalog = Catalog("pack ada.one 1.0.0\nmodule one(rate = 2) = {\n  out tone = sine(freq: rate)\n}\n");

        catalog.Get("ada.one.one").ShouldNotBeNull().Outputs.ShouldHaveSingleItem().Name.ShouldBe("tone");
        Fingerprint(Built("ada.one.one(rate: 4).tone |> out.left", catalog), catalog)
            .ShouldBe(Fingerprint(Built("sine(freq: 4) |> out.left", NodeCatalog.BuiltIn), NodeCatalog.BuiltIn));
    }

    [Fact]
    public void A_body_without_out_lines_keeps_the_plain_names()
    {
        var catalog = Catalog("pack ada.two 1.0.0\nmodule two(rate = 2) = {\n  (sine(freq: rate), saw(freq: rate))\n}\n");

        catalog.Get("ada.two.two").ShouldNotBeNull().Outputs.Select(p => p.Name).ShouldBe(["out_1", "out_2"]);
    }

    [Fact]
    public void Two_outputs_by_one_name_are_refused()
    {
        Read("module m(v = 1) = {\n  out a = v\n  out a = v * 2\n}\n")
            .ShouldHaveSingleItem().Code.ShouldBe(IssueCode.ModuleDuplicateOutput);
    }

    [Fact]
    public void Only_a_module_names_its_outputs()
    {
        var load = PatchLanguage.Build("def d(v = 1) = {\n  out a = v\n}\nd() |> out.left\n", NodeCatalog.BuiltIn);

        load.Issues.ShouldContain(i => i.Code == IssueCode.Syntax && i.Message.Contains("only a module's body"));
    }

    [Fact]
    public void Nothing_follows_the_out_lines()
    {
        Read("module m(v = 1) = {\n  out a = v\n  let b = 2\n}\n")
            .ShouldContain(i => i.Message.Contains("nothing follows them"));
    }

    [Fact]
    public void An_out_line_may_close_the_body_on_its_own_line()
    {
        var catalog = Catalog("pack ada.short 1.0.0\nmodule short(v = 1) = { out a = sine(freq: v) \"A tone.\" }\n");

        catalog.Get("ada.short.short").ShouldNotBeNull().Outputs.ShouldHaveSingleItem().Help.ShouldBe("A tone.");
    }
}
