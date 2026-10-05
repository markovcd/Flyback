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
}
