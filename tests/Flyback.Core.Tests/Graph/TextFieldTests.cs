using System.Text.Json.Nodes;
using Flyback.Core.Graph;
using Shouldly;

namespace Flyback.Core.Tests.Graph;

/// <summary>
/// <see cref="ExtraField.Text"/> — what somebody typed, kept as they typed it,
/// on one line or several.
/// </summary>
public class TextFieldTests
{
    private static ExtraField.Text Lines() => new("lines", "lines", "Hello", Multiline: true);

    private static ExtraField.Text Formula() => new("formula", "formula", "a");

    [Fact]
    public void What_was_typed_is_what_comes_back()
    {
        Lines().Value(JsonValue.Create("one\ntwo")).ShouldBe("one\ntwo");
        Formula().Value(JsonValue.Create("a * b")).ShouldBe("a * b");
    }

    /// <summary>
    /// Cleared on purpose is a value: a caption emptied in the panel stays empty
    /// rather than springing back to the greeting.
    /// </summary>
    [Fact]
    public void Empty_is_kept_rather_than_falling_back()
    {
        Lines().Value(JsonValue.Create(string.Empty)).ShouldBe(string.Empty);
    }

    [Fact]
    public void Nothing_stored_or_something_that_is_not_a_string_falls_back()
    {
        Lines().Value(null).ShouldBe("Hello");
        Lines().Value(JsonValue.Create(3)).ShouldBe("Hello");
    }

    /// <summary>
    /// A box on Windows types a carriage return and a line feed, and a file may
    /// hold either; lines break one way, whoever typed them.
    /// </summary>
    [Fact]
    public void Several_lines_break_one_way_whoever_typed_them()
    {
        Lines().Value(JsonValue.Create("one\r\ntwo")).ShouldBe("one\ntwo");
    }

    /// <summary>
    /// One line is kept exactly as typed: what it means is the module's to say.
    /// </summary>
    [Fact]
    public void One_line_is_kept_exactly_as_typed()
    {
        Formula().Value(JsonValue.Create("a\r\nb\t")).ShouldBe("a\r\nb\t");
    }

    [Fact]
    public void A_value_past_the_limit_is_cut()
    {
        var essay = new string('x', ExtraField.Text.Limit + 50);

        Lines().Value(JsonValue.Create(essay)).Length.ShouldBe(ExtraField.Text.Limit);
    }

    /// <summary>
    /// Reported with its breaks escaped, so an assistant reading a listing sees
    /// one value rather than a sentence that stops halfway.
    /// </summary>
    [Fact]
    public void Several_lines_are_reported_with_their_breaks_escaped()
    {
        Lines().Format(JsonValue.Create("one\ntwo")).ShouldBe("one\\ntwo");
        Formula().Format(JsonValue.Create("a + b")).ShouldBe("a + b");
    }

    [Fact]
    public void A_state_reads_it_back_by_key_and_nothing_for_another_shape()
    {
        var fields = new ExtraField[] { Lines(), new ExtraField.Toggle("on", "on") };
        var state = new ExtraState(fields, new JsonObject { ["lines"] = "a\nb" });

        state.Text("lines").ShouldBe("a\nb");
        state.Text("on").ShouldBe(string.Empty);
    }
}
