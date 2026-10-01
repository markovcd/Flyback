using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Shouldly;
using Xunit;

namespace Flyback.Plugins.Tests;

/// <summary>
/// What a turn promises whichever provider it runs over, driven through a
/// conversation that is only a script.
/// </summary>
public class TurnLoopTests
{
    /// <summary>A gray field: the smallest patch that may be proposed.</summary>
    private static readonly ToolCall[] Building =
    [
        new("a", "add_module", """{"type_id":"value","handle":"knob1","knobs":[{"port":"value","value":0.5}]}"""),
        new("b", "connect", """{"from":"knob1","to":"output1","to_port":"color"}"""),
    ];

    private static readonly ToolCall Proposing = new("p", "propose", """{"summary":"a gray field"}""");

    private static PatchWorkbench Bench() => new(NodeCatalog.BuiltIn, new Patch(), vision: false);

    private static async Task<List<PatchEvent>> Turn(
        PatchWorkbench bench,
        Scripted conversation,
        CancellationTokenSource? stop = null,
        Func<PatchEvent, bool>? stopAt = null,
        string instruction = "make something")
    {
        var events = new List<PatchEvent>();

        conversation.Workbench = bench;

        await foreach (var happened in TurnLoop.Run(conversation, instruction, stop?.Token ?? TestContext.Current.CancellationToken))
        {
            events.Add(happened);

            if (stop is not null && stopAt is not null && stopAt(happened)) await stop.CancelAsync();
        }

        return events;
    }

    [Fact]
    public async Task A_proposal_ends_the_turn_with_the_patch_it_offered()
    {
        var bench = Bench();
        var conversation = new Scripted(new ModelReply(null, Building), new ModelReply(null, [Proposing]));

        var events = await Turn(bench, conversation);

        events.OfType<PatchEvent.Proposed>().ShouldHaveSingleItem().Summary.ShouldBe("a gray field");
        conversation.Sent.ShouldBe(2);
    }

    [Fact]
    public async Task Calls_after_a_proposal_in_one_batch_are_answered_and_not_run()
    {
        var bench = Bench();
        var conversation = new Scripted(
            new ModelReply(null, Building),
            new ModelReply(null, [Proposing, new ToolCall("c", "add_module", """{"type_id":"value","handle":"knob2"}""")]));

        var events = await Turn(bench, conversation);

        events.OfType<PatchEvent.Proposed>().ShouldHaveSingleItem().Patch.Nodes.Count.ShouldBe(2);
        conversation.Answered[^1].Select(answer => answer.Call.Id).ShouldBe(["p", "c"]);
        conversation.Answered[^1][1].Text.ShouldContain("already proposed");
    }

    [Fact]
    public async Task A_stop_between_two_calls_still_answers_every_call()
    {
        var bench = Bench();
        var conversation = new Scripted(new ModelReply(null, Building), new ModelReply("never asked", []));
        using var stop = new CancellationTokenSource();

        await Turn(bench, conversation, stop, happened => happened is PatchEvent.Did);

        conversation.Answered.ShouldHaveSingleItem().Count.ShouldBe(2);
        conversation.Answered[0][1].Text.ShouldContain("stopped");
        conversation.Sent.ShouldBe(1, "nothing is asked after a stop");
    }

    /// <summary>Asked once to propose what it built and still not proposing, the turn says the canvas has not changed.</summary>
    [Fact]
    public async Task A_turn_that_built_and_offered_nothing_says_so()
    {
        var bench = Bench();
        var conversation = new Scripted(new ModelReply(null, Building), new ModelReply("done", []), new ModelReply("not yet: it is too dark", []));

        var events = await Turn(bench, conversation);

        events.OfType<PatchEvent.Did>().ShouldContain(did => did.Summary.Contains("canvas still shows"));
        conversation.Sent.ShouldBe(3, "asked to propose once, and only once");
    }

    /// <summary>A model that built the patch and stopped without offering it is asked once, and the patch reaches the person.</summary>
    [Fact]
    public async Task A_turn_that_built_and_stopped_is_asked_once_to_propose_it()
    {
        var conversation = new Scripted(new ModelReply(null, Building), new ModelReply("It is a gray field.", []), new ModelReply(null, [Proposing]));

        var events = await Turn(Bench(), conversation);

        events.OfType<PatchEvent.Proposed>().ShouldHaveSingleItem();
        conversation.Log.ShouldContain(line => line.StartsWith("add: [From Flyback") && line.Contains("propose"));
    }

    /// <summary>A question is a fair way to end a turn that built something, so it is not argued with.</summary>
    [Fact]
    public async Task A_turn_that_built_and_asked_a_question_is_not_asked_to_propose()
    {
        var conversation = new Scripted(new ModelReply(null, Building), new ModelReply("Should the gray be warmer or colder?", []));

        var events = await Turn(Bench(), conversation);

        conversation.Sent.ShouldBe(2);
        events.OfType<PatchEvent.Did>().ShouldContain(did => did.Summary.Contains("canvas still shows"));
    }

    [Fact]
    public async Task A_turn_that_only_talked_after_one_that_built_says_nothing_about_the_canvas()
    {
        var bench = Bench();

        await Turn(bench, new Scripted(new ModelReply(null, Building), new ModelReply("done", []), new ModelReply("still done", [])));
        var second = await Turn(bench, new Scripted(new ModelReply("It is gray.", [])));

        second.OfType<PatchEvent.Did>().ShouldBeEmpty();
    }

    /// <summary>
    /// Asked to listen or to reach a loudness with no ear, the turn says it cannot
    /// before the model says anything, and the model is told so with the message.
    /// </summary>
    [Theory]
    [InlineData("listen to it and bring it to -16 LUFS")]
    [InlineData("How does it sound? Can you hear the bass?")]
    [InlineData("make the loudness about -14")]
    public async Task Asked_to_listen_with_no_ear_the_turn_says_it_cannot(string asked)
    {
        var conversation = new Scripted(new ModelReply("done", []));

        var events = await Turn(Bench(), conversation, instruction: asked);

        events[0].ShouldBeOfType<PatchEvent.Did>().Summary.ShouldContain("cannot hear");
        conversation.Log.ShouldContain(line => line.StartsWith("add: ") && line.Contains("cannot hear") && line.EndsWith(asked));
    }

    [Fact]
    public async Task Asked_to_listen_with_an_ear_the_turn_says_nothing_about_it()
    {
        var conversation = new Scripted(new ModelReply("done", []));
        var hearing = new PatchWorkbench(NodeCatalog.BuiltIn, new Patch(), vision: false, hearing: Listener.Another);

        var events = await Turn(hearing, conversation, instruction: "listen to it and bring it to -16 LUFS");

        events.ShouldBe([new PatchEvent.Said("done")]);
        conversation.Log.ShouldContain("add: listen to it and bring it to -16 LUFS");
    }

    [Fact]
    public async Task Asked_for_something_else_with_no_ear_the_turn_says_nothing_about_hearing()
    {
        var conversation = new Scripted(new ModelReply("done", []));

        var events = await Turn(Bench(), conversation, instruction: "make it bluer and louder");

        events.ShouldBe([new PatchEvent.Said("done")]);
        conversation.Log.ShouldContain("add: make it bluer and louder");
    }

    [Fact]
    public async Task Pictures_from_an_earlier_turn_are_forgotten_before_the_next_message()
    {
        var conversation = new Scripted(new ModelReply("hello", []));

        await Turn(Bench(), conversation);

        conversation.Log.Take(2).ShouldBe(["forget", "add: make something"]);
    }

    [Fact]
    public async Task Arguments_that_will_not_read_are_refused_rather_than_thrown()
    {
        var conversation = new Scripted(new ModelReply(null, [new ToolCall("x", "set_knobs", "{not json")]), new ModelReply("sorry", []));

        await Turn(Bench(), conversation);

        conversation.Answered[0][0].Text.ShouldContain("not valid JSON");
    }

    [Fact]
    public async Task A_knob_a_float_cannot_hold_is_refused_rather_than_thrown()
    {
        var conversation = new Scripted(new ModelReply(null,
        [
            new ToolCall("a", "add_module", """{"type_id":"value","handle":"knob1","knobs":[{"port":"value","value":1e39}]}"""),
            Building[1],
            Proposing,
        ]));

        await Turn(Bench(), conversation);

        conversation.Answered[0][0].Text.ShouldContain("float");
    }

    [Fact]
    public async Task A_refused_request_is_the_turn_failing_not_an_exception()
    {
        var events = await Turn(Bench(), new Scripted());

        events.ShouldHaveSingleItem().ShouldBeOfType<PatchEvent.Failed>().Message.ShouldBe("no more replies");
    }

    /// <summary>A caller that asks the session itself gets the same turn the host runs.</summary>
    [Fact]
    public async Task Asking_the_session_itself_runs_the_same_turn()
    {
        var conversation = new Scripted(new ModelReply(null, Building), new ModelReply(null, [Proposing])) { Workbench = Bench() };
        var events = new List<PatchEvent>();

        await foreach (var happened in ((IPatchSession)conversation).Ask("make something", TestContext.Current.CancellationToken))
            events.Add(happened);

        events.OfType<PatchEvent.Proposed>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_model_that_never_stops_asking_runs_into_the_fuse()
    {
        var asking = new ModelReply(null, [new ToolCall("d", "describe_patch", "{}")]);
        var conversation = new Scripted([.. Enumerable.Repeat(asking, TurnLoop.MaxExchanges + 1)]);

        var events = await Turn(Bench(), conversation);

        events[^1].ShouldBeOfType<PatchEvent.Failed>().Message.ShouldContain("exchanges");
        conversation.Sent.ShouldBe(TurnLoop.MaxExchanges);
    }

    /// <summary>A send that waits out a rate limit says so while it waits, not once it is done.</summary>
    [Fact]
    public async Task A_wait_for_the_rate_limit_is_told_while_it_waits()
    {
        var limited = new TaskCompletionSource();
        var conversation = new Scripted(new ModelReply("hello", []))
        {
            Workbench = Bench(),
            Before = async () =>
            {
                AssistantPost.Tell(TimeSpan.FromSeconds(30.2), 429);
                await limited.Task;
            },
        };

        await using var turn = TurnLoop.Run(conversation, "make something", TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        (await turn.MoveNextAsync()).ShouldBeTrue();
        turn.Current.ShouldBe(new PatchEvent.Did("waiting 31s for the rate limit"));
        limited.Task.IsCompleted.ShouldBeFalse();

        limited.SetResult();

        (await turn.MoveNextAsync()).ShouldBeTrue();
        turn.Current.ShouldBe(new PatchEvent.Said("hello"));
    }

    /// <summary>A hiccup of under a second is not worth a line.</summary>
    [Fact]
    public async Task A_wait_of_under_a_second_is_not_told()
    {
        var conversation = new Scripted(new ModelReply("hello", []))
        {
            Before = () =>
            {
                AssistantPost.Tell(TimeSpan.FromSeconds(0.5), 429);
                return Task.CompletedTask;
            },
        };

        var events = await Turn(Bench(), conversation);

        events.ShouldBe([new PatchEvent.Said("hello")]);
    }

    /// <summary>Replies in order, and keeps what it was told.</summary>
    private sealed class Scripted(params ModelReply[] replies) : IModelConversation
    {
        private readonly Queue<ModelReply> replies = new(replies);

        public int Sent { get; private set; }

        public List<IReadOnlyList<ToolAnswer>> Answered { get; } = [];

        public List<string> Log { get; } = [];

        public PatchWorkbench Workbench { get; set; } = Bench();

        public bool HearsItself => false;

        public string? EarModel => null;

        public void Forget() => Log.Add("forget");

        public void Add(string instruction) => Log.Add("add: " + instruction);

        /// <summary>What a send does before it answers, as a provider waiting out a refusal would.</summary>
        public Func<Task>? Before { get; init; }

        public async Task<ModelReply> Send(CancellationToken cancel)
        {
            Sent++;

            if (Before is { } before) await before();

            return replies.Count > 0 ? replies.Dequeue() : throw new HttpRequestException("no more replies");
        }

        public void Add(IReadOnlyList<ToolAnswer> answers) => Answered.Add(answers);

        public Task<string?> Listen(string model, string briefing, byte[] wav, CancellationToken cancel) =>
            Task.FromResult<string?>("a hum");

        public void Dispose()
        {
        }
    }
}
