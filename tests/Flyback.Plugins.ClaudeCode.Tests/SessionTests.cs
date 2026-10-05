using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.ClaudeCode.Tests;

/// <summary>A turn driven by the host's loop over canned replies from Claude Code.</summary>
public class SessionTests
{
    private const string Building = """
        Starting with a gray field.
        <calls>
        [{"name":"add_module","arguments":{"type_id":"value","handle":"knob1","knobs":[{"port":"value","value":0.5}]}},
         {"name":"connect","arguments":{"from":"knob1","to":"output1","to_port":"color"}},
         {"name":"propose","arguments":{"summary":"a gray field"}}]
        </calls>
        """;

    private static ClaudeCodeSession Session(ScriptedCli cli) => new(
        new PatchWorkbench(NodeCatalog.BuiltIn, new Patch(), vision: false),
        new AssistantChoices("sonnet"),
        cli);

    private static async Task<List<PatchEvent>> Drain(IPatchSession session, string instruction)
    {
        List<PatchEvent> events = [];

        await foreach (var happened in session.Ask(instruction, TestContext.Current.CancellationToken))
            events.Add(happened);

        return events;
    }

    [Fact]
    public async Task A_turn_builds_and_proposes_through_the_calls_in_the_reply()
    {
        using var session = Session(new ScriptedCli(Building));

        var events = await Drain(session, "make a gray field");

        events.OfType<PatchEvent.Said>().ShouldHaveSingleItem().Text.ShouldBe("Starting with a gray field.");
        events.OfType<PatchEvent.Proposed>().ShouldHaveSingleItem().Patch.Nodes.Count.ShouldBe(2);
        events.OfType<PatchEvent.Cost>().ShouldHaveSingleItem().ShouldBe(new PatchEvent.Cost(100, 50, 10));
    }

    [Fact]
    public async Task What_the_workbench_answered_goes_back_in_the_next_question()
    {
        var cli = new ScriptedCli(
            """<calls>[{"name":"add_module","arguments":{"type_id":"value","handle":"knob1"}}]</calls>""",
            "Done for now.");

        using var session = Session(cli);

        await Drain(session, "add a value");

        cli.Sent.Count.ShouldBe(3);
        ScriptedCli.TextOf(cli.Sent[1]).ShouldContain("<person>");
        cli.LastText.ShouldContain("<you>");
        cli.LastText.ShouldContain("Call 1 (add_module):");
        cli.LastText.ShouldContain("knob1");
    }

    [Fact]
    public async Task A_block_that_would_not_read_is_answered_with_why_and_the_turn_goes_on()
    {
        var cli = new ScriptedCli("<calls>[oops</calls>", Building);

        using var session = Session(cli);

        var events = await Drain(session, "make a gray field");

        ScriptedCli.TextOf(cli.Sent[1]).ShouldContain("not valid JSON");
        events.OfType<PatchEvent.Proposed>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_failure_is_a_failed_event_and_not_an_exception()
    {
        using var session = Session(new ScriptedCli());

        var events = await Drain(session, "anything");

        events.OfType<PatchEvent.Failed>().ShouldHaveSingleItem().Message.ShouldBe("nothing left to say.");
    }

    [Fact]
    public async Task The_briefing_leads_every_question_and_is_the_same_bytes_each_time()
    {
        var cli = new ScriptedCli(
            """<calls>[{"name":"describe_patch"}]</calls>""",
            "ok");

        using var session = Session(cli);

        await Drain(session, "look");

        var first = (string?)cli.Sent[0][0]!["text"];

        first.ShouldNotBeNullOrWhiteSpace();
        first.ShouldContain("## describe_patch");
        ((string?)cli.Sent[1][0]!["text"]).ShouldBe(first);
    }

    [Fact]
    public async Task A_saved_conversation_carries_on_without_its_pictures()
    {
        var first = Session(new ScriptedCli(Building));

        await Drain(first, "make a gray field");

        var saved = first.Save();

        saved.ShouldNotBeNull();

        var cli = new ScriptedCli("Sure.");
        var second = Session(cli);

        second.Take(saved).ShouldBeTrue();

        await Drain(second, "and brighter");

        cli.LastText.ShouldContain("make a gray field");
        cli.LastText.ShouldContain("and brighter");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""[{"role":"system","text":"x"}]""")]
    [InlineData("""[{"role":"you"}]""")]
    public void Something_that_is_not_a_saved_conversation_is_refused_whole(string saved)
    {
        Session(new ScriptedCli()).Take(saved).ShouldBeFalse();
    }

    [Fact]
    public void A_saved_conversation_holds_no_picture()
    {
        IModelConversation conversation = Session(new ScriptedCli());

        conversation.Add([new ToolAnswer(new ToolCall("c", "render", "{}"), "drew it", [1, 2, 3])]);

        ((ClaudeCodeSession)conversation).Save().ShouldContain(ClaudeCodeSession.PictureGone);
        ((ClaudeCodeSession)conversation).Save().ShouldNotContain("AQID");
    }
}
