using Flyback.Core.Graph;
using Flyback.Engine.Language;
using Shouldly;

namespace Flyback.Core.Tests.Language;

/// <summary>
/// What the text offers where the cursor is, marked <c>¦</c> in each source.
/// </summary>
public class CompletionTests
{
    private static CompletionList At(string marked)
    {
        var at = marked.IndexOf('¦');
        var source = marked.Remove(at, 1);
        var before = source[..at];
        var line = before.Count(c => c == '\n') + 1;
        var column = at - (before.LastIndexOf('\n') + 1) + 1;

        return Completions.At(source, line, column, NodeCatalog.BuiltIn);
    }

    private static string[] Labels(string marked) => [.. At(marked).Items.Select(i => i.Label)];

    private static Completion Item(string marked, string label) => At(marked).Items.Single(i => i.Label == label);

    [Fact]
    public void A_call_offers_its_modules_sockets_with_their_colon()
    {
        var freq = Item("sine(fr¦", "freq");

        freq.Kind.ShouldBe(CompletionKind.Socket);
        freq.Insert.ShouldBe("freq: ");
        freq.Detail.ShouldStartWith("Cycles for each unit of 'in'");
    }

    [Fact]
    public void A_socket_the_call_already_names_is_not_offered_again()
    {
        var labels = Labels("sine(freq: 2, ¦");

        labels.ShouldNotContain("freq");
        labels.ShouldContain("amp");
    }

    [Fact]
    public void A_call_piped_into_leaves_out_the_in_the_pipe_fills()
    {
        Labels("x |> sine(¦").ShouldNotContain("in");
        Labels("sine(¦").ShouldContain("in");
    }

    [Fact]
    public void A_value_in_a_call_offers_modules_names_and_the_pipes_placeholder()
    {
        var labels = Labels("let wobble = sine(freq: 3)\nsaw(freq: ¦");

        labels.ShouldContain("wobble");
        labels.ShouldContain("sine");
        labels.ShouldContain("_");
        labels.ShouldNotContain("freq");
    }

    [Fact]
    public void The_word_typed_so_far_narrows_the_list_and_says_where_it_starts()
    {
        var list = At("x |> kalei¦");

        list.Typed.ShouldBe("kalei");
        list.Column.ShouldBe(6);
        list.Items.Select(i => i.Label).ShouldBe(["kaleidoscope"]);
    }

    [Fact]
    public void A_pipe_offers_stages_not_keywords()
    {
        var labels = Labels("x |> ¦");

        labels.ShouldContain("out");
        labels.ShouldContain("rotate");
        labels.ShouldNotContain("let");
    }

    [Fact]
    public void A_line_start_offers_the_statement_words()
    {
        Labels("le¦").ShouldContain("let");
        Labels("sine(freq: 2) |> out.left\nde¦").ShouldContain("def");
    }

    [Fact]
    public void The_Output_offers_its_sockets_after_a_dot()
    {
        var labels = Labels("sine(freq: 2) |> out.¦");

        labels.ShouldContain("left");
        labels.ShouldContain("color");
        labels.ShouldContain("volume");
    }

    [Fact]
    public void A_call_offers_its_outputs_after_a_dot()
    {
        var beats = Item("let beats = tempo(bpm: 104).¦", "beats");

        beats.Kind.ShouldBe(CompletionKind.Output);
    }

    [Fact]
    public void A_name_bound_to_a_module_offers_its_outputs_and_on_its_own_line_its_knobs()
    {
        const string Riff = "let riff = notes(rate: 4) [ A3 C4 ]\n";

        var read = Labels(Riff + "riff.gate |> adsr(gate: riff.¦");
        read.ShouldContain("gate");
        read.ShouldNotContain("rate");

        var set = Labels(Riff + "riff.¦");
        set.ShouldContain("gate");
        set.ShouldContain("rate");
    }

    [Fact]
    public void A_dotted_type_id_offers_the_rest_of_its_name()
    {
        Labels("color.¦").ShouldContain("mix");
    }

    [Fact]
    public void A_defs_call_offers_its_parameters()
    {
        var labels = Labels("def voice(note, hue = 0) = sine(freq: note)\nvoice(hue: 1, ¦");

        labels.ShouldContain("note");
        labels.ShouldNotContain("hue");
        Item("def voice(note, hue = 0) = sine(freq: note)\nvoice(¦", "note").Insert.ShouldBe("note: ");
    }

    [Fact]
    public void A_parameter_is_in_sight_only_in_its_defs_body()
    {
        Labels("def voice(pitch) = sine(freq: pi¦").ShouldContain("pitch");
        Labels("def voice(pitch) = sine(freq: pitch)\nsaw(freq: pi¦").ShouldNotContain("pitch");
        Labels("def voice(pitch) = {\n  let s = sine(freq: pitch)\n  s |> drive(drive: pi¦").ShouldContain("pitch");
    }

    [Fact]
    public void A_groups_names_outlive_it_and_a_loops_do_not()
    {
        const string Source =
            """
            group "Bass" {
              let bass = sine(freq: 55)
            }
            let voices = for pitch in [110, 220] {
              let tone = saw(freq: pitch)
              tone
            }

            """;

        var labels = Labels(Source + "¦");

        labels.ShouldContain("bass");
        labels.ShouldContain("voices");
        labels.ShouldNotContain("tone");
        labels.ShouldNotContain("pitch");
    }

    [Fact]
    public void A_def_written_in_a_block_stays_in_it()
    {
        const string Source =
            """
            group "Lead" {
              def twice(f) = f * 2
              let lead = sine(freq: twice(220))
            }

            """;

        var labels = Labels(Source + "¦");

        labels.ShouldContain("lead");
        labels.ShouldNotContain("twice");
    }

    [Fact]
    public void Nothing_is_offered_in_a_comment_a_text_a_step_block_or_a_number()
    {
        Labels("# sin¦").ShouldBeEmpty();
        Labels("description \"sin¦").ShouldBeEmpty();
        Labels("notes(rate: 4) [ A3 C¦").ShouldBeEmpty();
        Labels("sine(freq: 2¦").ShouldBeEmpty();
    }

    [Fact]
    public void A_new_name_is_not_completed()
    {
        Labels("let si¦").ShouldBeEmpty();
        Labels("def ¦").ShouldBeEmpty();
    }

    [Fact]
    public void Every_module_is_offered_by_the_name_the_text_reads_it_as()
    {
        var offered = Labels("¦")
            .Where(l => At("¦").Items.Single(i => i.Label == l).Kind == CompletionKind.Module)
            .ToArray();

        foreach (var label in offered)
            PatchLanguage.Build($"let m = {label}()\n", NodeCatalog.BuiltIn).Issues
                .ShouldNotContain(i => i.Code == IssueCode.UnknownModule || i.Code == IssueCode.AmbiguousModule, label);

        offered.Length.ShouldBe(NodeCatalog.BuiltIn.All.Count - 1);
    }
}
