using Flyback.Core.Compile;
using Flyback.Core.Graph;
using Flyback.Core.Graph.Extras;
using Flyback.Engine.Compile;
using Flyback.Engine.Language;
using Flyback.Engine.Packs;
using Shouldly;

namespace Flyback.Core.Tests.Language;

/// <summary>A pack module's socket ranges, and its choices, which are settings built one body each.</summary>
public class PackSettingsTests
{
    private const string Shapes = """
        pack ada.shapes 1.0.0

        module shaped(rate = 2 in 0.5..20, level = 0.5 in 0..1, wave = "sine" in ["sine", "saw"], loud = 1 in [1, 2]) = {
          if wave == "saw" { saw(freq: rate, amp: loud / 4) } else { sine(freq: rate, amp: loud / 4) }
        }
        """;

    private static ModuleCatalog Catalog(string source = Shapes)
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

    private static LanguageIssue Refused(string module)
    {
        var (_, issues) = PackModules.Into(NodeCatalog.BuiltIn, "pack ada.bad 1.0.0\n" + module);

        return issues.ShouldHaveSingleItem();
    }

    [Fact]
    public void A_range_is_how_far_the_socket_turns()
    {
        var def = Catalog().Get("ada.shapes.shaped").ShouldNotBeNull();

        def.Inputs.Select(p => (p.Name, p.Default, p.Min, p.Max)).ShouldBe([("rate", 2f, 0.5f, 20f), ("level", 0.5f, 0f, 1f)]);
    }

    [Fact]
    public void Choices_are_settings_rather_than_sockets()
    {
        var settings = Catalog().Get("ada.shapes.shaped").ShouldNotBeNull().Extra<SettingsExtra>().ShouldNotBeNull();

        settings.Fields.Cast<ExtraField.Choice>().Select(f => (f.Key, f.Fallback, string.Join(",", f.Options.Select(o => o.Id))))
            .ShouldBe([("wave", "sine", "sine,saw"), ("loud", "1", "1,2")]);
    }

    [Fact]
    public void A_module_placed_fresh_is_built_at_its_defaults()
    {
        var catalog = Catalog();

        Fingerprint(Built("ada.shapes.shaped(rate: 5) |> out.left", catalog), catalog)
            .ShouldBe(Fingerprint(Built("sine(freq: 5, amp: 0.25) |> out.left", NodeCatalog.BuiltIn), NodeCatalog.BuiltIn));
    }

    [Fact]
    public void Each_setting_builds_its_own_body()
    {
        var catalog = Catalog();
        var patch = Built("ada.shapes.shaped(rate: 5) |> out.left", catalog);
        var placed = patch.Nodes.Single(n => n.TypeId == "ada.shapes.shaped");

        placed.SetState(PackModules.SettingsKey, new System.Text.Json.Nodes.JsonObject { ["wave"] = "saw", ["loud"] = "2" });

        Fingerprint(patch, catalog)
            .ShouldBe(Fingerprint(Built("saw(freq: 5, amp: 0.5) |> out.left", NodeCatalog.BuiltIn), NodeCatalog.BuiltIn));
    }

    [Fact]
    public void The_text_picks_a_setting_by_name_as_it_does_a_plugin_module_s()
    {
        var catalog = Catalog();

        Fingerprint(Built("ada.shapes.shaped(rate: 5, wave: \"saw\", loud: 2) |> out.left", catalog), catalog)
            .ShouldBe(Fingerprint(Built("saw(freq: 5, amp: 0.5) |> out.left", NodeCatalog.BuiltIn), NodeCatalog.BuiltIn));
    }

    [Fact]
    public void A_setting_no_choice_names_is_built_at_its_default()
    {
        var catalog = Catalog();
        var patch = Built("ada.shapes.shaped(rate: 5) |> out.left", catalog);

        patch.Nodes.Single(n => n.TypeId == "ada.shapes.shaped")
            .SetState(PackModules.SettingsKey, new System.Text.Json.Nodes.JsonObject { ["wave"] = "square" });

        Fingerprint(patch, catalog)
            .ShouldBe(Fingerprint(Built("sine(freq: 5, amp: 0.25) |> out.left", NodeCatalog.BuiltIn), NodeCatalog.BuiltIn));
    }

    [Fact]
    public void A_def_called_in_the_pack_keeps_its_range_and_choices_to_itself() =>
        Built("""
            def louder(v = 1 in 0..2, how = "a" in ["a", "b"]) = v * 2
            louder(v: 0.5) |> out.left
            """, NodeCatalog.BuiltIn).Nodes.ShouldNotBeEmpty();

    [Fact]
    public void A_range_written_high_to_low_is_refused() =>
        Refused("module m(v = 1 in 2..0) = v").Code.ShouldBe(IssueCode.ModuleBadRange);

    [Fact]
    public void A_default_outside_its_range_is_refused() =>
        Refused("module m(v = 5 in 0..1) = v").Code.ShouldBe(IssueCode.ModuleDefaultOutsideRange);

    [Fact]
    public void Choices_mixing_numbers_and_texts_are_refused() =>
        Refused("module m(v = 1, how = 1 in [1, \"two\"]) = v").Code.ShouldBe(IssueCode.ModuleBadChoices);

    [Fact]
    public void A_default_that_is_none_of_the_choices_is_refused() =>
        Refused("module m(v = 1, how = 3 in [1, 2]) = v").Code.ShouldBe(IssueCode.ModuleDefaultOutsideRange);

    [Fact]
    public void Choices_combining_more_ways_than_a_module_is_built_are_refused() =>
        Refused("module m(v = 1, a = 1 in [1, 2, 3, 4, 5, 6, 7, 8], b = 1 in [1, 2, 3, 4, 5, 6, 7, 8, 9]) = v").Code.ShouldBe(IssueCode.ModuleTooManyChoices);

    [Fact]
    public void A_text_default_without_choices_is_refused() =>
        Refused("module m(v = \"a\") = v").Code.ShouldBe(IssueCode.ModuleDefaultNotANumber);

    [Fact]
    public void In_without_a_default_says_how_it_is_written()
    {
        var load = PatchLanguage.Build("def m(v in 0..1) = v\nm(v: 1) |> out.left");

        load.Issues.ShouldContain(issue => issue.Code == IssueCode.Syntax);
    }

    [Fact]
    public void In_followed_by_neither_a_range_nor_a_list_says_how_it_is_written()
    {
        var load = PatchLanguage.Build("def m(v = 0 in 3) = v\nm() |> out.left");

        load.Issues.ShouldContain(issue => issue.Code == IssueCode.Syntax && issue.Message.Contains("a range or a list of choices"));
    }
}
