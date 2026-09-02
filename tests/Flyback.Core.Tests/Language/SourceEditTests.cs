using Flyback.Core.Graph;
using Flyback.Core.Language;
using Shouldly;

namespace Flyback.Core.Tests.Language;

/// <summary>
/// Setting a knob in a source file, in the place the file already says it.
/// </summary>
/// <remarks>
/// A patch printed again is a patch rewritten — the comments go, the defs are
/// expanded, the formatting is somebody else's — so a knob turned in the panel
/// cannot be answered by printing. It is answered by changing the one number
/// that moved, and everything here is about doing that to somebody else's file
/// without touching anything they wrote.
/// </remarks>
public class SourceEditTests
{
    private static string After(string source, string module, string port, string value)
    {
        var change = SourceEdit.Knob(source, module, port, value).ShouldNotBeNull();

        return source[..change.Offset] + change.Text + source[(change.Offset + change.Length)..];
    }

    private static void Builds(string source)
    {
        var load = PatchLanguage.Build(source, NodeCatalog.BuiltIn);

        load.Issues.ShouldBeEmpty(load.Report);
    }

    // --- changing what is already written -----------------------------------

    [Fact]
    public void A_knob_the_call_already_names_is_changed_in_place()
    {
        After("let sine = x |> sine(freq: 1.5)\nsine |> out.color", "sine", "freq", "6.46")
            .ShouldBe("let sine = x |> sine(freq: 6.46)\nsine |> out.color");
    }

    [Fact]
    public void One_of_several_arguments_is_changed_and_the_rest_left_alone()
    {
        After(
            "let sine = x |> sine(freq: 1.5, amp: 0.5, bias: 0.25)\nsine |> out.color",
            "sine",
            "amp",
            "0.9")
            .ShouldContain("sine(freq: 1.5, amp: 0.9, bias: 0.25)");
    }

    /// <summary>
    /// The call the binding names is the last one: `let hum = t |> sine() |>
    /// gain()` is a gain called hum, and the sine before it has no name at all.
    /// </summary>
    [Fact]
    public void The_call_changed_is_the_one_the_name_is_bound_to()
    {
        After(
            "let hum = t |> sine(freq: 220) |> gain(gain: 0.5)\nhum |> out.left",
            "hum",
            "gain",
            "0.8")
            .ShouldContain("|> gain(gain: 0.8)");
    }

    /// <summary>A statement broken across lines is still one statement.</summary>
    [Fact]
    public void A_call_wrapped_over_several_lines_is_still_found()
    {
        var wrapped = After(
            """
            let remap = x |> remap(in_low: -2, in_high: 2)
            let hsv = remap
              |> color.hsv(
                  saturation: 0.85,
                  value: 1)

            hsv |> out.color
            """,
            "hsv",
            "saturation",
            "0.4");

        wrapped.ShouldContain("saturation: 0.4,");
        Builds(wrapped);
    }

    /// <summary>Nothing outside the one number moves, comments included.</summary>
    [Fact]
    public void Nothing_else_in_the_file_is_touched()
    {
        const string source = """
            # a tone I am rather fond of
            let hum = t |> sine(freq: 220)

            hum |> out.left
            """;

        var after = After(source, "hum", "freq", "330");

        after.ShouldBe(source.Replace("220", "330"));
    }

    // --- adding what is not written -----------------------------------------

    /// <summary>
    /// A knob sitting at its default is not in the text at all, so it is added
    /// to the call rather than said again somewhere else.
    /// </summary>
    [Fact]
    public void A_knob_the_call_does_not_name_is_added_to_it()
    {
        var after = After("let sine = x |> sine(freq: 1.5)\nsine |> out.color", "sine", "amp", "0.3");

        after.ShouldContain("sine(freq: 1.5, amp: 0.3)");
        Builds(after);
    }

    [Fact]
    public void A_call_with_no_arguments_takes_the_first_one()
    {
        var after = After("let noise = x |> noise()\nnoise |> out.color", "noise", "scale", "2.5");

        after.ShouldContain("noise(scale: 2.5)");
        Builds(after);
    }

    // --- when it will not ---------------------------------------------------

    /// <summary>
    /// An argument with no name is filling its socket by position, and adding a
    /// named one for the same socket would say it twice. Nothing here knows
    /// which position is which port — that is the binder's — so such a call is
    /// left alone and the caller says it another way.
    /// </summary>
    [Fact]
    public void A_call_written_by_position_is_left_alone()
    {
        SourceEdit.Knob("let a = x |> remap(-2..2, 0..1)\na |> out.color", "a", "out_low", "0.2")
            .ShouldBeNull();
    }

    [Fact]
    public void A_module_the_text_never_bound_is_not_found()
    {
        SourceEdit.Knob("x |> sine(freq: 1.5) |> out.color", "sine", "freq", "2")
            .ShouldBeNull();
    }

    [Fact]
    public void A_binding_that_is_not_a_call_is_left_alone()
    {
        SourceEdit.Knob("let slowly = t * 0.2\nslowly |> out.color", "slowly", "b", "3")
            .ShouldBeNull();
    }

    /// <summary>
    /// A name that begins another name is not that name. `sine` and `sine2` are
    /// two modules, and turning one must not reach into the other.
    /// </summary>
    [Fact]
    public void A_name_that_begins_another_is_not_mistaken_for_it()
    {
        const string source = """
            let sine2 = y |> sine(freq: 1.1)
            let sine = x |> sine(freq: 1.5)

            sine |> add(b: sine2) |> out.color
            """;

        After(source, "sine", "freq", "9").ShouldContain("let sine = x |> sine(freq: 9)");
        After(source, "sine", "freq", "9").ShouldContain("let sine2 = y |> sine(freq: 1.1)");
    }
}
