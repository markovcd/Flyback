using Flyback.Core.Graph;
using Flyback.Engine.Language;
using Shouldly;

namespace Flyback.Core.Tests.Language;

/// <summary>What the word under the caret is, marked <c>¦</c> in each source, and the signature of the call it is in.</summary>
public class HoverTests
{
    private static (string Source, int Line, int Column) Marked(string marked)
    {
        var at = marked.IndexOf('¦');
        var before = marked[..at];

        return (marked.Remove(at, 1), before.Count(c => c == '\n') + 1, at - (before.LastIndexOf('\n') + 1) + 1);
    }

    private static Hover? Hover(string marked)
    {
        var (source, line, column) = Marked(marked);
        return Hovers.At(source, line, column, NodeCatalog.BuiltIn);
    }

    private static Signature? Signature(string marked)
    {
        var (source, line, column) = Marked(marked);
        return Signatures.At(source, line, column, NodeCatalog.BuiltIn);
    }

    [Fact]
    public void A_module_says_what_it_is_and_its_sockets()
    {
        var hover = Hover("si¦ne(freq: 2) |> out.left").ShouldNotBeNull();

        hover.Title.ShouldBe("sine · Sine (osc.sine)");
        hover.Body.ShouldStartWith("The basic waveform.");
        hover.Body.ShouldContain("Inputs: in, freq, phase, amp, bias. Outputs: out.");
    }

    [Fact]
    public void A_socket_named_in_a_call_says_its_range_and_what_it_is_for()
    {
        var hover = Hover("sine(fr¦eq: 2)").ShouldNotBeNull();

        hover.Title.ShouldBe("freq · input of Sine · 0 to 20000, at 1");
        hover.Body.ShouldStartWith("Cycles for each unit of 'in'");
    }

    [Fact]
    public void A_socket_after_the_Outputs_dot_is_the_Outputs()
    {
        Hover("sine(freq: 2) |> out.le¦ft").ShouldNotBeNull().Title.ShouldStartWith("left · input of Output");
    }

    [Fact]
    public void An_output_after_a_bound_name_is_the_modules_output()
    {
        Hover("let beat = tempo(bpm: 90)\nbeat.be¦ats |> out.left").ShouldNotBeNull().Title.ShouldBe("beats · output of Tempo");
    }

    [Fact]
    public void A_dotted_type_id_is_its_module_wherever_the_caret_is_in_it()
    {
        Hover("co¦lor.mix(a: x)").ShouldNotBeNull().Title.ShouldContain("(color.mix)");
        Hover("color.m¦ix(a: x)").ShouldNotBeNull().Title.ShouldContain("(color.mix)");
    }

    [Fact]
    public void A_name_the_text_bound_says_what_it_placed_and_where()
    {
        const string source = "let lfo = sine(freq: 0.5)\nsaw(freq: lf¦o)";

        Hover(source).ShouldNotBeNull().Title.ShouldBe("lfo · Sine, bound on line 1");
        Hover("let lf¦o = sine(freq: 0.5)").ShouldNotBeNull().Title.ShouldBe("lfo · Sine, bound on line 1");
        Hover("def voice(note, hue = 0) = sine(freq: note)\nvoi¦ce(220)").ShouldNotBeNull().Title.ShouldBe("def voice(note, hue)");
    }

    [Fact]
    public void The_languages_own_names_say_what_they_are()
    {
        Hover("sine(freq: ¦t)").ShouldNotBeNull().Title.ShouldBe("t · the clock");
        Hover("¦x |> sine(freq: 2)").ShouldNotBeNull().Title.ShouldContain("coordinates");
    }

    [Fact]
    public void A_comment_a_number_or_an_unknown_word_says_nothing()
    {
        Hover("// a si¦ne").ShouldBeNull();
        Hover("sine(freq: 2¦20)").ShouldBeNull();
        Hover("nothi¦ng(1)").ShouldBeNull();
    }

    [Fact]
    public void A_signature_lists_the_sockets_and_marks_the_one_named()
    {
        var signature = Signature("sine(amp: ¦").ShouldNotBeNull();

        signature.Title.ShouldBe("Sine (osc.sine)");
        signature.Sockets.Select(s => s.Name).ShouldBe(["in", "freq", "phase", "amp", "bias"]);
        signature.Sockets[signature.Active].Name.ShouldBe("amp");
        signature.Piped.ShouldBe(-1);
    }

    [Fact]
    public void An_argument_with_no_name_goes_to_the_next_socket_nothing_else_fills()
    {
        var signature = Signature("x |> smoothstep(0.1, ¦").ShouldNotBeNull();

        signature.Sockets[signature.Piped].Name.ShouldBe("in");
        signature.Sockets[signature.Active].Name.ShouldBe("edge1");
    }

    [Fact]
    public void An_underscore_moves_the_pipe_to_its_socket()
    {
        var signature = Signature("beat |> adsr(gate: _, decay: ¦").ShouldNotBeNull();

        signature.Sockets[signature.Piped].Name.ShouldBe("gate");
        signature.Sockets[signature.Active].Name.ShouldBe("decay");
    }

    [Fact]
    public void A_defs_call_lists_its_parameters()
    {
        var signature = Signature("def voice(note, hue = 0) = sine(freq: note)\nvoice(220, ¦").ShouldNotBeNull();

        signature.Title.ShouldBe("def voice");
        signature.Sockets[signature.Active].Name.ShouldBe("hue");
    }

    [Fact]
    public void Outside_a_call_there_is_no_signature()
    {
        Signature("sine(freq: 2) ¦").ShouldBeNull();
        Signature("let a = ¦").ShouldBeNull();
    }
}
