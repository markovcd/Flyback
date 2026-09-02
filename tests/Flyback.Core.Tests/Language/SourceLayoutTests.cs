using Flyback.Core.Graph;
using Flyback.Core.Language;
using Shouldly;

namespace Flyback.Core.Tests.Language;

/// <summary>
/// Where the lines break, which is worked out after the text is written the way
/// where the modules sit is worked out after the graph is built.
/// </summary>
/// <remarks>
/// The printer wrote one line per statement, and a statement is a whole
/// pipeline: Plasma came out at 154 characters and Whole band at 540. What is
/// checked here is that breaking them changes only where the newlines are —
/// <see cref="PrinterTests"/> already holds every preset to building back
/// opcode for opcode, and it is that pair of facts together that makes this
/// safe to run over anything.
/// </remarks>
public class SourceLayoutTests
{
    private static string[] Lines(string source) =>
        source.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');

    private static string Build(string source)
    {
        var load = PatchLanguage.Build(source, NodeCatalog.BuiltIn);

        load.Issues.ShouldBeEmpty(load.Report);

        return PatchIO.ToJson(load.Patch, NodeCatalog.BuiltIn);
    }

    // --- what it leaves alone -----------------------------------------------

    [Fact]
    public void A_line_that_fits_is_left_exactly_as_it_was()
    {
        const string source = "let hum = t |> sine(freq: 220)\nhum |> out.left\n";

        SourceLayout.Wrap(source).ShouldBe(source);
    }

    [Fact]
    public void Nothing_is_joined_back_up()
    {
        const string source = "x\n  |> sine(freq: 1.5)\n  |> out.color\n";

        SourceLayout.Wrap(source).ShouldBe(source);
    }

    /// <summary>
    /// A comment has no structure to break on and breaking one would put half a
    /// sentence into the patch as code.
    /// </summary>
    [Fact]
    public void A_long_comment_is_left_long()
    {
        var comment = "# " + new string('a', 200);

        SourceLayout.Wrap(comment).ShouldBe(comment);
    }

    [Fact]
    public void An_empty_source_stays_empty()
    {
        SourceLayout.Wrap(string.Empty).ShouldBe(string.Empty);
    }

    // --- where it breaks ----------------------------------------------------

    /// <summary>
    /// The shape every pipeline in the handbook is written in: what is being
    /// read and its first stage together, then one stage a line.
    /// </summary>
    [Fact]
    public void A_long_pipeline_breaks_before_each_stage()
    {
        var wrapped = SourceLayout.Wrap(
            "x |> sine(freq: 1.5) |> add(b: 0.25) |> remap(in_low: -2, in_high: 2) "
            + "|> color.hsv(saturation: 0.85) |> out.color",
            width: 40);

        Lines(wrapped).ShouldBe([
            "x |> sine(freq: 1.5)",
            "  |> add(b: 0.25)",
            "  |> remap(in_low: -2, in_high: 2)",
            "  |> color.hsv(saturation: 0.85)",
            "  |> out.color",
        ]);
    }

    /// <summary>
    /// A pipe inside a call belongs to that call. Breaking there would take an
    /// argument out of the brackets it is an argument of.
    /// </summary>
    [Fact]
    public void A_pipe_inside_a_call_is_not_a_place_to_break()
    {
        var wrapped = SourceLayout.Wrap(
            "x |> add(b: y |> sine(freq: 1.1)) |> out.color",
            width: 30);

        // Broken somewhere, since none of it fits — but the argument's own
        // pipeline is intact on one line, which is the claim. Broken there, the
        // second half would be outside the brackets it is inside.
        Lines(wrapped).ShouldContain(line => line.Contains("y |> sine(freq: 1.1)"));
        Lines(wrapped)[^1].ShouldBe("  |> out.color");
    }

    /// <summary>
    /// And at a width the stage does fit in, it stays whole — so the break above
    /// was the width's doing rather than the inner pipe's.
    /// </summary>
    [Fact]
    public void A_stage_that_fits_is_one_line_however_many_pipes_are_inside_it()
    {
        var wrapped = SourceLayout.Wrap(
            "x |> add(b: y |> sine(freq: 1.1)) |> out.color",
            width: 40);

        Lines(wrapped).ShouldBe([
            "x |> add(b: y |> sine(freq: 1.1))",
            "  |> out.color",
        ]);
    }

    /// <summary>
    /// Where the pipeline was not what made it long. Whole band prints one
    /// mixer of five hundred characters, and no amount of breaking around it
    /// would help.
    /// </summary>
    [Fact]
    public void A_call_with_many_arguments_breaks_after_each_comma()
    {
        var wrapped = SourceLayout.Wrap("mixer(level_1: a, level_2: b, level_3: c)", width: 30);

        Lines(wrapped).ShouldBe([
            "mixer(",
            "    level_1: a,",
            "    level_2: b,",
            "    level_3: c)",
        ]);
    }

    /// <summary>
    /// A tune is one token however many lines it spans — the lexer scans a block
    /// to its closing bracket and counts the newlines it swallowed, so a
    /// complaint after a long tune still points at the right line.
    /// </summary>
    [Fact]
    public void A_long_tune_is_filled_across_lines()
    {
        var wrapped = SourceLayout.Wrap(
            "let riff = notes(rate: clock) [ A4 C5 E5 G5 B5 D6 F6 A6 ]",
            width: 32);

        Lines(wrapped).ShouldBe([
            "let riff = notes(rate: clock) [",
            "    A4 C5 E5 G5 B5 D6 F6 A6 ]",
        ]);
    }

    /// <summary>
    /// A sharp is not a comment. `C#4` is one word to the lexer because it
    /// begins on a note letter, and a pass that stopped reading at the hash
    /// would leave everything after a sharpened note unbroken.
    /// </summary>
    [Fact]
    public void A_sharpened_note_does_not_end_the_line()
    {
        var wrapped = SourceLayout.Wrap(
            "sine(freq: note(note: C#4)) |> gain(gain: 0.5) |> out.left",
            width: 40);

        Lines(wrapped).Length.ShouldBeGreaterThan(1);
        Lines(wrapped)[^1].ShouldBe("  |> out.left");
    }

    // --- and it is still the same patch -------------------------------------

    /// <summary>
    /// The property the whole thing rests on. Every break made here is one the
    /// lexer joins straight back up, so wrapping a source cannot change what it
    /// describes — checked against the patch file, which is exact, rather than
    /// against the text.
    /// </summary>
    [Theory]
    [MemberData(nameof(Names))]
    public void Wrapping_a_preset_does_not_change_the_patch_it_describes(string name)
    {
        var patch = Presets.All.Single(preset => preset.Name == name).Build(NodeCatalog.BuiltIn);
        var printed = PatchPrinter.Print(patch, NodeCatalog.BuiltIn);

        // Narrow enough to force a break into every statement that has one in
        // it, which is far narrower than anything the printer would choose.
        Build(SourceLayout.Wrap(printed, width: 30)).ShouldBe(Build(printed));
    }

    /// <summary>And nothing it writes is longer than it was asked for.</summary>
    [Theory]
    [MemberData(nameof(Names))]
    public void No_preset_prints_a_line_wider_than_the_page(string name)
    {
        var patch = Presets.All.Single(preset => preset.Name == name).Build(NodeCatalog.BuiltIn);

        foreach (var line in Lines(PatchPrinter.Print(patch, NodeCatalog.BuiltIn)))
            line.Length.ShouldBeLessThanOrEqualTo(SourceLayout.Width, line);
    }

    public static TheoryData<string> Names => [.. Presets.All.Select(preset => preset.Name)];
}
