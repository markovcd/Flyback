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
        Func<PatchEvent, bool>? stopAt = null)
    {
        var events = new List<PatchEvent>();

        conversation.Workbench = bench;

        await foreach (var happened in TurnLoop.Run(conversation, "make something", stop?.Token ?? TestContext.Current.CancellationToken))
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

    [Fact]
    public async Task A_turn_that_built_and_offered_nothing_says_so()
    {
        var bench = Bench();

        var events = await Turn(bench, new Scripted(new ModelReply(null, Building), new ModelReply("done", [])));

        events.OfType<PatchEvent.Did>().ShouldContain(did => did.Summary.Contains("canvas still shows"));
    }

    [Fact]
    public async Task A_turn_that_only_talked_after_one_that_built_says_nothing_about_the_canvas()
    {
        var bench = Bench();

        await Turn(bench, new Scripted(new ModelReply(null, Building), new ModelReply("done", [])));
        var second = await Turn(bench, new Scripted(new ModelReply("It is gray.", [])));

        second.OfType<PatchEvent.Did>().ShouldBeEmpty();
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

        public Task<ModelReply> Send(CancellationToken cancel)
        {
            Sent++;

            return replies.Count > 0
                ? Task.FromResult(replies.Dequeue())
                : Task.FromException<ModelReply>(new HttpRequestException("no more replies"));
        }

        public void Add(IReadOnlyList<ToolAnswer> answers) => Answered.Add(answers);

        public Task<string?> Listen(string model, string briefing, byte[] wav, CancellationToken cancel) =>
            Task.FromResult<string?>("a hum");

        public void Dispose()
        {
        }
    }
}
