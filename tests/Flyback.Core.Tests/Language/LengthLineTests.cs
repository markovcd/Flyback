using Flyback.Core.Graph;
using Flyback.Core.Language;
using Shouldly;

namespace Flyback.Core.Tests.Language;

/// <summary>
/// <c>length 2:30.50</c>: how long the patch plays for, which the patch carries and the
/// seek bar spans.
/// </summary>
public class LengthLineTests
{
    private const string Tone = "sine() |> out.left";

    private static LanguageLoad Built(string source)
    {
        var load = PatchLanguage.Build(source, NodeCatalog.BuiltIn);

        load.Issues.ShouldBeEmpty(load.Report);

        return load;
    }

    private static string Applied(string source, Change change) =>
        source[..change.Offset] + change.Text + source[(change.Offset + change.Length)..];

    [Theory]
    [InlineData("length 2:30.50", 150.5)]
    [InlineData("length 2:30", 150d)]
    [InlineData("length 0:00.25", 0.25)]
    [InlineData("length 95.5", 95.5)]
    [InlineData("length 90s", 90d)]
    [InlineData("length 1500ms", 1.5)]
    public void A_length_line_says_how_long_the_patch_plays(string line, double seconds) =>
        Built(line + "\n" + Tone).Patch.Length.ShouldBe(seconds);

    [Fact]
    public void Saying_nothing_leaves_the_default() =>
        Built(Tone).Patch.Length.ShouldBeNull();

    [Theory]
    [InlineData("length 0")]
    [InlineData("length 0.05")]
    [InlineData("length 1.5:00")]
    [InlineData("length 1441:00")]
    [InlineData("length 1440:00.01")]
    public void A_length_that_is_not_one_is_refused(string line)
    {
        var load = PatchLanguage.Build(line + "\n" + Tone, NodeCatalog.BuiltIn);

        load.Issues.ShouldContain(issue => issue.Code == IssueCode.BadLength, load.Report);
        load.Patch.Length.ShouldBeNull();
    }

    [Fact]
    public void A_binding_called_length_is_still_a_name()
    {
        var patch = Built("let length = sine()\nlength |> out.left").Patch;

        patch.Length.ShouldBeNull();
        patch.Nodes.ShouldContain(n => n.TypeId == "osc.sine");
    }

    [Fact]
    public void Saying_it_twice_is_refused()
    {
        var load = PatchLanguage.Build("length 1:00\nlength 2:00\n" + Tone, NodeCatalog.BuiltIn);

        load.Issues.ShouldContain(issue => issue.Code == IssueCode.SaidTwice);
        load.Patch.Length.ShouldBe(60);
    }

    [Fact]
    public void A_printing_says_the_length_in_minutes_and_builds_back_to_it()
    {
        var patch = Built(Tone).Patch;
        patch.Length = 125.25;

        var source = PatchPrinter.Print(patch, NodeCatalog.BuiltIn);

        source.ShouldStartWith("length 2:05.25");
        Built(source).Patch.Length.ShouldBe(125.25);
    }

    [Fact]
    public void The_default_prints_nothing() =>
        PatchPrinter.Print(Built(Tone).Patch, NodeCatalog.BuiltIn).ShouldNotContain("length");

    [Fact]
    public void Adding_the_line_leaves_every_module_its_identity()
    {
        var without = Built("sine() |> saw() |> out.left").Patch;
        var with = Built("length 1:00\nsine() |> saw() |> out.left").Patch;

        with.Nodes.Select(n => n.Id).ShouldBe(without.Nodes.Select(n => n.Id));
    }

    [Fact]
    public void The_seek_bar_writes_a_new_line_at_the_top()
    {
        const string source = Tone + "\n";

        var change = Built(source).Map.Length("length 2:30.00");

        Applied(source, change!.Value).ShouldBe("length 2:30.00\n\n" + Tone + "\n");
    }

    [Fact]
    public void A_new_line_goes_under_the_tags()
    {
        const string source = "description \"a tone\"\ntags \"drone\"\n\n" + Tone + "\n";

        var change = Built(source).Map.Length("length 2:30.00");

        Applied(source, change!.Value).ShouldBe("description \"a tone\"\ntags \"drone\"\nlength 2:30.00\n\n" + Tone + "\n");
    }

    [Fact]
    public void The_seek_bar_changes_the_whole_line_where_it_stands()
    {
        const string source = "length 1:00.50\n" + Tone + "\n";

        var change = Built(source).Map.Length("length 3:15.00");

        Applied(source, change!.Value).ShouldBe("length 3:15.00\n" + Tone + "\n");
    }

    [Fact]
    public void A_text_that_already_says_it_is_left_alone() =>
        Built("length 1:00.00\n" + Tone).Map.Length("length 1:00.00").ShouldBeNull();

    [Fact]
    public void A_saved_patch_keeps_its_length()
    {
        var patch = Built(Tone).Patch;
        patch.Length = 42.42;

        PatchIO.Read(PatchIO.ToJson(patch, NodeCatalog.BuiltIn), NodeCatalog.BuiltIn).Patch.Length.ShouldBe(42.42);
    }

    [Fact]
    public void The_default_is_not_written_into_the_file() =>
        PatchIO.ToJson(Built(Tone).Patch, NodeCatalog.BuiltIn)
            .ShouldNotContain($"\"{nameof(Patch.Length)}\"", Case.Sensitive);

    [Fact]
    public void A_length_is_kept_to_the_hundredth_and_inside_a_day()
    {
        new Patch { Length = 1.23456 }.Length.ShouldBe(1.23);
        new Patch { Length = 0 }.Length.ShouldBe(PatchLength.Shortest);
        new Patch { Length = 1e9 }.Length.ShouldBe(PatchLength.Longest);
        new Patch { Length = double.NaN }.Length.ShouldBeNull();
    }

    [Theory]
    [InlineData("90", 90d)]
    [InlineData("1:30", 90d)]
    [InlineData(" 2:05 ", 125d)]
    [InlineData("1:30.4", 90.4)]
    [InlineData("1:30.456", 90.46)]
    [InlineData("10:00", 600d)]
    [InlineData("0.5", 0.5)]
    public void A_typed_length_is_seconds_or_minutes_and_seconds(string typed, double seconds) =>
        PatchLength.Read(typed).ShouldBe(seconds);

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("1:2:3")]
    [InlineData("1.5:00")]
    [InlineData("100000")]
    public void A_typed_length_a_patch_cannot_keep_is_turned_away(string typed) => PatchLength.Read(typed).ShouldBeNull();

    [Theory]
    [InlineData(90d, "1:30.00")]
    [InlineData(150.5, "2:30.50")]
    [InlineData(0.07, "0:00.07")]
    [InlineData(59.999, "1:00.00")]
    public void A_length_is_shown_the_way_the_status_bar_tells_the_time(double seconds, string shown) =>
        PatchLength.Say(seconds).ShouldBe(shown);
}
