using Flyback.Core.Graph;
using Flyback.Core.Language;
using Shouldly;

namespace Flyback.Core.Tests.Language;

/// <summary><c>description "..."</c>: what the patch is for, which the patch carries rather than any module in it.</summary>
public class DescriptionLineTests
{
    private static LanguageLoad Built(string source)
    {
        var load = PatchLanguage.Build(source, NodeCatalog.BuiltIn);

        load.Issues.ShouldBeEmpty(load.Report);

        return load;
    }

    private static string Applied(string source, Change change) =>
        source[..change.Offset] + change.Text + source[(change.Offset + change.Length)..];

    [Fact]
    public void A_description_line_describes_the_patch()
    {
        Built("description \"A slow tide, heard and seen.\"\nsine(freq: 220) |> out.left")
            .Patch.Description.ShouldBe("A slow tide, heard and seen.");
    }

    [Fact]
    public void Saying_nothing_is_no_description()
    {
        Built("sine(freq: 220) |> out.left").Patch.Description.ShouldBeNull();
    }

    [Fact]
    public void A_binding_called_description_is_still_a_name()
    {
        var patch = Built("let description = sine(freq: 220)\ndescription |> out.left").Patch;

        patch.Description.ShouldBeNull();
        patch.Nodes.ShouldContain(n => n.TypeId == NodeCatalog.SineTypeId);
    }

    [Fact]
    public void Saying_it_twice_is_refused()
    {
        var load = PatchLanguage.Build(
            "description \"one\"\ndescription \"two\"\nsine() |> out.left", NodeCatalog.BuiltIn);

        load.Issues.ShouldContain(issue => issue.Message.Contains("already described"));
        load.Patch.Description.ShouldBe("one");
    }

    [Fact]
    public void A_printing_says_it_first_and_builds_back_to_it()
    {
        var patch = Built("keyboard scale [ C D E F G A B ]\nmidi.in().pitch |> out.left").Patch;
        patch.Describe("Two notes, over and over, for a very long time indeed, until somebody stops them, which nobody will.");

        var source = PatchPrinter.Print(patch, NodeCatalog.BuiltIn);

        source.ShouldStartWith("description \"Two notes,");
        source.ShouldContain("\n\nkeyboard scale [ C D E F G A B ]");
        Built(source).Patch.Description.ShouldBe(patch.Description);
    }

    [Fact]
    public void A_long_one_runs_on_over_lines_that_fit_the_page()
    {
        var patch = Built("sine() |> out.left").Patch;
        patch.Describe(string.Join(' ', Enumerable.Repeat("a hum that goes on", 20)));

        var source = PatchPrinter.Print(patch, NodeCatalog.BuiltIn);

        source.Split('\n').ShouldAllBe(line => line.Length <= SourceLayout.Width);
        source.Split('\n')[1].ShouldStartWith("  \"");
        Built(source).Patch.Description.ShouldBe(patch.Description);
    }

    [Fact]
    public void The_panel_replaces_every_line_of_one_that_runs_on()
    {
        const string source = "description \"a slow\"\n  \"hum\"\nsine() |> out.left\n";

        Built(source).Patch.Description.ShouldBe("a slow hum");

        var change = Built(source).Map.Description("description \"drone\"");

        Applied(source, change!.Value).ShouldBe("description \"drone\"\nsine() |> out.left\n");
    }

    [Fact]
    public void Adding_the_line_leaves_every_module_its_identity()
    {
        var without = Built("sine() |> out.left").Patch;
        var with = Built("description \"hum\"\nsine() |> out.left").Patch;

        with.Nodes.Select(n => n.Id).ShouldBe(without.Nodes.Select(n => n.Id));
    }

    [Fact]
    public void The_panel_writes_a_new_line_at_the_top()
    {
        const string source = "sine() |> out.left\n";

        var change = Built(source).Map.Description("description \"hum\"");

        Applied(source, change!.Value).ShouldBe("description \"hum\"\n\nsine() |> out.left\n");
    }

    [Fact]
    public void The_panel_changes_the_line_where_it_stands()
    {
        const string source = "# lead\ndescription \"hum\"\nsine() |> out.left\n";

        var change = Built(source).Map.Description("description \"drone\"");

        Applied(source, change!.Value).ShouldBe("# lead\ndescription \"drone\"\nsine() |> out.left\n");
    }

    [Fact]
    public void Emptying_it_takes_the_line_out()
    {
        const string source = "description \"hum\"\nsine() |> out.left\n";

        var change = Built(source).Map.Description(null);

        Applied(source, change!.Value).ShouldBe("sine() |> out.left\n");
    }

    [Fact]
    public void A_text_that_already_says_it_is_left_alone()
    {
        Built("description \"hum\"\nsine() |> out.left").Map.Description("description \"hum\"").ShouldBeNull();
    }

    [Fact]
    public void A_saved_patch_keeps_it()
    {
        var patch = Built("sine() |> out.left").Patch;
        patch.Describe("hum");

        PatchIO.Read(PatchIO.ToJson(patch, NodeCatalog.BuiltIn), NodeCatalog.BuiltIn)
            .Patch.Description.ShouldBe("hum");
    }

    [Fact]
    public void None_is_not_written_into_the_file()
    {
        PatchIO.ToJson(Built("sine() |> out.left").Patch, NodeCatalog.BuiltIn)
            .ShouldNotContain(nameof(Patch.Description));
    }

    [Fact]
    public void It_is_one_line_with_its_quotes_turned()
    {
        var patch = new Patch();

        patch.Describe("  a \"loud\"\r\n  hum  ");
        patch.Description.ShouldBe("a “loud” hum");

        patch.Describe("   ");
        patch.Description.ShouldBeNull();

        patch.Describe(new string('x', Patch.DescriptionLimit + 50));
        patch.Description!.Length.ShouldBe(Patch.DescriptionLimit);
    }

    [Fact]
    public void A_shipped_preset_builds_carrying_its_description()
    {
        var plasma = Presets.All.Single(p => p.Name == "Plasma");

        plasma.Build(NodeCatalog.BuiltIn).Description.ShouldBe(plasma.Description);
    }

    [Fact]
    public void A_patch_that_describes_itself_keeps_its_own()
    {
        var preset = new PatchPreset("Own", _ =>
        {
            var patch = new Patch();
            patch.Describe("mine");
            return patch;
        }, "the preset's");

        preset.Build(NodeCatalog.BuiltIn).Description.ShouldBe("mine");
    }
}
