using Flyback.Plugins.Assist;
using Flyback.Plugins.Programs;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Programs.Tests;

/// <summary>How tools are offered in words and called in a block at the end of a reply.</summary>
public class ProtocolTests
{
    [Fact]
    public void A_reply_with_no_block_is_only_words()
    {
        var (text, calls) = Protocol.Parse("  It is a pad.  ");

        text.ShouldBe("It is a pad.");
        calls.ShouldBeEmpty();
    }

    [Fact]
    public void A_block_at_the_end_is_the_calls_in_order()
    {
        var (text, calls) = Protocol.Parse("""
            Adding two things.
            <calls>
            [{"name":"add_module","arguments":{"type_id":"value"}},
             {"name":"connect","arguments":{"from":"a","to":"b"}}]
            </calls>
            """);

        text.ShouldBe("Adding two things.");
        calls.Select(c => c.Name).ShouldBe(["add_module", "connect"]);
        calls[0].Arguments.ShouldBe("""{"type_id":"value"}""");
        calls.Select(c => c.Id).Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public void A_call_with_no_arguments_gets_an_empty_object()
    {
        Protocol.Parse("""<calls>[{"name":"describe_patch"}]</calls>""").Calls
            .ShouldHaveSingleItem().Arguments.ShouldBe("{}");
    }

    [Fact]
    public void Arguments_written_as_a_string_are_taken_as_they_are()
    {
        Protocol.Parse("""<calls>[{"name":"x","arguments":"{\"a\":1}"}]</calls>""").Calls
            .ShouldHaveSingleItem().Arguments.ShouldBe("""{"a":1}""");
    }

    [Theory]
    [InlineData("""<calls>[{"name":"a"}]""", "never closed")]
    [InlineData("<calls>not json</calls>", "not valid JSON")]
    [InlineData("""<calls>{"name":"a"}</calls>""", "JSON array")]
    [InlineData("<calls>[{\"arguments\":{}}]</calls>", "\"name\"")]
    public void A_block_that_will_not_read_is_one_rejected_call_that_says_why(string reply, string why)
    {
        var call = Protocol.Parse(reply).Calls.ShouldHaveSingleItem();

        call.Name.ShouldBe(Protocol.Malformed);
        call.Arguments.ShouldContain(why);
    }

    [Fact]
    public void A_rejected_call_is_answered_with_why_and_not_with_the_workbenchs_refusal()
    {
        var call = new ToolCall("call1", Protocol.Malformed, "nothing ran: bad.");

        Protocol.Answers([new ToolAnswer(call, "there is no tool called 'malformed_calls'.")])
            .ShouldBe("Call 1 (malformed_calls): nothing ran: bad.");
    }

    [Fact]
    public void The_preamble_carries_the_briefing_and_every_tool()
    {
        var preamble = Protocol.Preamble(
            "BRIEFING",
            [new PatchTool("add_module", "Adds one.", """{"properties":{}}""")]);

        preamble.ShouldStartWith("BRIEFING");
        preamble.ShouldContain("## add_module");
        preamble.ShouldContain("Adds one.");
        preamble.ShouldContain(Protocol.Open);
    }
}
