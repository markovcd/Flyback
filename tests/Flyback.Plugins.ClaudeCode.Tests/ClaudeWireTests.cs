using Flyback.Plugins.Programs;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.ClaudeCode.Tests;

/// <summary>How a conversation is written as the content of one message.</summary>
public class ClaudeWireTests
{
    [Fact]
    public void A_picture_goes_after_the_turn_it_belongs_to_as_a_base64_block()
    {
        var content = ClaudeWire.Content("PRE", [new Turn(Turn.Flyback, "seen", [[1, 2, 3]])]);

        content.Count.ShouldBe(4);
        content[1]!["text"]!.GetValue<string>().ShouldContain("<flyback>");
        content[2]!["type"]!.GetValue<string>().ShouldBe("image");
        content[2]!["source"]!["data"]!.GetValue<string>().ShouldBe("AQID");
    }

    [Fact]
    public void The_conversation_so_far_is_marked_for_the_cache_and_the_prompt_to_answer_is_not()
    {
        var content = ClaudeWire.Content("PRE", [new Turn(Turn.Person, "hi", []), new Turn(Turn.Flyback, "seen", [[1, 2, 3]])]);

        content.Select(block => block!["cache_control"] is not null).ShouldBe([false, false, false, true, false]);
        content[3]!["type"]!.GetValue<string>().ShouldBe("image");
        content[3]!["cache_control"]!["ttl"]!.GetValue<string>().ShouldBe("1h");
    }

    [Fact]
    public void With_no_turns_yet_the_briefing_is_what_is_marked()
    {
        var content = ClaudeWire.Content("PRE", []);

        content[0]!["cache_control"].ShouldNotBeNull();
    }
}
