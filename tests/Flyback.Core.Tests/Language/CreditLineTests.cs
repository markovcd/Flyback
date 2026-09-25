using Flyback.Core.Graph;
using Flyback.Core.Language;
using Shouldly;

namespace Flyback.Core.Tests.Language;

/// <summary><c>author "..."</c> and <c>tags "..."</c>: who made the patch and words to find it by.</summary>
public class CreditLineTests
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
    public void An_author_line_credits_the_patch()
    {
        Built("author \"Ada\"\nsine(freq: 220) |> out.left").Patch.Author.ShouldBe("Ada");
    }

    [Fact]
    public void A_tags_line_tags_the_patch()
    {
        Built("tags \"drone\" \"Slow Build\"\nsine(freq: 220) |> out.left")
            .Patch.Tags.ShouldBe(["drone", "slow-build"]);
    }

    [Fact]
    public void Saying_nothing_is_no_author_and_no_tags()
    {
        var patch = Built("sine(freq: 220) |> out.left").Patch;

        patch.Author.ShouldBeNull();
        patch.Tags.ShouldBeNull();
    }

    [Fact]
    public void Bindings_called_author_and_tags_are_still_names()
    {
        var patch = Built("let author = sine(freq: 220)\nlet tags = sine(freq: 3)\nauthor + tags |> out.left").Patch;

        patch.Author.ShouldBeNull();
        patch.Tags.ShouldBeNull();
        patch.Nodes.ShouldContain(n => n.TypeId == NodeCatalog.SineTypeId);
    }

    [Fact]
    public void Saying_either_twice_is_refused()
    {
        var load = PatchLanguage.Build(
            "author \"one\"\nauthor \"two\"\ntags \"a\"\ntags \"b\"\nsine() |> out.left", NodeCatalog.BuiltIn);

        load.Issues.ShouldContain(issue => issue.Message.Contains("already credited"));
        load.Issues.ShouldContain(issue => issue.Message.Contains("already tagged"));
        load.Patch.Author.ShouldBe("one");
        load.Patch.Tags.ShouldBe(["a"]);
    }

    [Fact]
    public void A_printing_says_them_under_the_description_and_builds_back_to_them()
    {
        var patch = Built("keyboard scale [ C D E F G A B ]\nmidi.in().pitch |> out.left").Patch;
        patch.Describe("Two notes.");
        patch.Credit("Ada");
        patch.Tag(["drone", "slow"]);

        var source = PatchPrinter.Print(patch, NodeCatalog.BuiltIn);

        source.ShouldStartWith("description \"Two notes.\"\nauthor \"Ada\"\ntags \"drone\" \"slow\"\n\nkeyboard scale [ C D E F G A B ]");

        var back = Built(source).Patch;
        back.Author.ShouldBe("Ada");
        back.Tags.ShouldBe(["drone", "slow"]);
    }

    [Fact]
    public void A_printing_with_no_description_starts_with_what_it_does_say()
    {
        var patch = Built("sine() |> out.left").Patch;
        patch.Tag(["hum"]);

        PatchPrinter.Print(patch, NodeCatalog.BuiltIn).ShouldStartWith("tags \"hum\"\n\n");
    }

    [Fact]
    public void The_panel_writes_a_new_author_under_the_description()
    {
        const string source = "description \"hum\"\n\nsine() |> out.left\n";

        var change = Built(source).Map.Author("author \"Ada\"");

        Applied(source, change!.Value).ShouldBe("description \"hum\"\nauthor \"Ada\"\n\nsine() |> out.left\n");
    }

    [Fact]
    public void The_panel_writes_new_tags_under_the_author()
    {
        const string source = "description \"hum\"\nauthor \"Ada\"\n\nsine() |> out.left\n";

        var change = Built(source).Map.Tags("tags \"drone\"");

        Applied(source, change!.Value)
            .ShouldBe("description \"hum\"\nauthor \"Ada\"\ntags \"drone\"\n\nsine() |> out.left\n");
    }

    [Fact]
    public void The_panel_writes_a_new_line_at_the_top_where_the_patch_says_nothing()
    {
        const string source = "sine() |> out.left\n";

        var change = Built(source).Map.Tags("tags \"drone\"");

        Applied(source, change!.Value).ShouldBe("tags \"drone\"\n\nsine() |> out.left\n");
    }

    [Fact]
    public void The_panel_replaces_every_tag_on_the_line()
    {
        const string source = "tags \"a\" \"b\" \"c\"\nsine() |> out.left\n";

        var change = Built(source).Map.Tags("tags \"d\"");

        Applied(source, change!.Value).ShouldBe("tags \"d\"\nsine() |> out.left\n");
    }

    [Fact]
    public void Emptying_the_author_takes_the_line_out()
    {
        const string source = "author \"Ada\"\nsine() |> out.left\n";

        var change = Built(source).Map.Author(null);

        Applied(source, change!.Value).ShouldBe("sine() |> out.left\n");
    }

    [Fact]
    public void A_text_that_already_says_them_is_left_alone()
    {
        var map = Built("author \"Ada\"\ntags \"a\" \"b\"\nsine() |> out.left").Map;

        map.Author("author \"Ada\"").ShouldBeNull();
        map.Tags("tags \"a\" \"b\"").ShouldBeNull();
    }

    [Fact]
    public void A_saved_patch_keeps_them()
    {
        var patch = Built("sine() |> out.left").Patch;
        patch.Credit("Ada");
        patch.Tag(["drone", "slow"]);

        var back = PatchIO.Read(PatchIO.ToJson(patch, NodeCatalog.BuiltIn), NodeCatalog.BuiltIn).Patch;

        back.Author.ShouldBe("Ada");
        back.Tags.ShouldBe(["drone", "slow"]);
    }

    [Fact]
    public void None_is_not_written_into_the_file()
    {
        var json = PatchIO.ToJson(Built("sine() |> out.left").Patch, NodeCatalog.BuiltIn);

        json.ShouldNotContain(nameof(Patch.Author));
        json.ShouldNotContain(nameof(Patch.Tags));
    }

    [Fact]
    public void An_author_is_one_line_with_its_quotes_turned()
    {
        var patch = new Patch();

        patch.Credit("  Ada \"the\"\r\n  Countess ");
        patch.Author.ShouldBe("Ada “the” Countess");

        patch.Credit(" ");
        patch.Author.ShouldBeNull();

        patch.Credit(new string('x', Patch.AuthorLimit + 5));
        patch.Author!.Length.ShouldBe(Patch.AuthorLimit);
    }

    [Fact]
    public void Tags_are_tidied_into_short_lower_case_words()
    {
        Patch.TidiedTags(["  Slow   Build ", "DRONE", "drone", "\"quoted\"", "", "   "])
            .ShouldBe(["slow-build", "drone", "quoted"]);

        Patch.TidiedTags([new string('x', Patch.TagLimit + 5)])!.Single().Length.ShouldBe(Patch.TagLimit);

        Patch.TidiedTags(Enumerable.Range(0, Patch.TagCount + 3).Select(i => "t" + i))!.Count.ShouldBe(Patch.TagCount);

        Patch.TidiedTags(["", " "]).ShouldBeNull();
        Patch.TidiedTags(null).ShouldBeNull();
    }
}
