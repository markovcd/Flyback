using Flyback.Plugins.Programs;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Codex.Tests;

/// <summary>How a conversation is written as the prompt.</summary>
public class CodexWireTests
{
    [Fact]
    public void A_picture_is_marked_after_the_turn_it_belongs_to_and_attached_in_order()
    {
        var (prompt, pictures) = CodexWire.Conversation(
            "PRE",
            [new Turn(Turn.Flyback, "seen", [[1, 2, 3]]), new Turn(Turn.Model, "ok", []), new Turn(Turn.Flyback, "again", [[4]])]);

        prompt.ShouldStartWith("PRE");
        prompt.ShouldContain("<flyback>\nseen\n</flyback>\n[Image #1 is attached to this turn.]");
        prompt.ShouldContain("<flyback>\nagain\n</flyback>\n[Image #2 is attached to this turn.]");
        prompt.ShouldEndWith("Write your next turn now.");
        pictures.ShouldBe([[1, 2, 3], [4]]);
    }

    [Fact]
    public void The_instructions_tell_the_model_it_is_not_a_coding_agent()
    {
        CodexWire.System.ShouldContain("AGENTS.md");
        CodexWire.System.ShouldContain("not a coding agent");
    }
}
