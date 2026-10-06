using System.Runtime.CompilerServices;
using System.Text.Json;
using Flyback.Editor.Assist;
using Flyback.Assist;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Settings;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Tests.Assist;

/// <summary>
/// The shell's half of a conversation, driven by a fake on the far side so the
/// test decides the timing. The same arrangement <c>LoopbackDevice</c> gives
/// <see cref="Flyback.Ui.Audio.AudioEngine"/>: no network, no Avalonia, no
/// waiting.
/// </summary>
public class AssistantRunTests
{
    private static AssistantRun RunOf(IPatchAssistant assistant, Patch? start = null, int maxContext = AssistantRun.ContextLimit) =>
        new(assistant,
            AssistantConfig.Unset,
            NodeCatalog.BuiltIn,
            start ?? new Patch(),
            maxContext);

    /// <summary>A turn that says hello and reports sending <paramref name="sent"/> tokens.</summary>
    private static ScriptedAssistant Sending(int sent) =>
        new(new PatchEvent.Said("hello"), new PatchEvent.Cost(sent, 0, 10));

    private static async Task<List<PatchEvent>> Drain(AssistantRun run, string instruction = "make something")
    {
        var events = new List<PatchEvent>();

        await foreach (var happened in run.Ask(instruction, TestContext.Current.CancellationToken))
            events.Add(happened);

        return events;
    }

    // --- the key --------------------------------------------------------------

    private const string Key = "sk-proj-abcdefghijklmnop";

    private static AssistantConfig Keyed =>
        new(new KeyedTransport(Key, "https://assistant.test", new AssistantCredential("", "")), SettingValues.None);

    [Fact]
    public async Task A_message_with_the_key_in_it_is_not_sent()
    {
        var assistant = new ScriptedAssistant(new PatchEvent.Said("thanks"));
        using var run = new AssistantRun(assistant, Keyed, NodeCatalog.BuiltIn, new Patch());

        var events = await Drain(run, $"my key is {Key}, make something");

        events.ShouldHaveSingleItem().ShouldBeOfType<PatchEvent.Failed>().Message.ShouldContain("has your key in it");
        assistant.Heard.ShouldBeEmpty();
        run.Turns.ShouldBe(0);
    }

    [Fact]
    public void A_saved_conversation_never_holds_the_key()
    {
        using var run = new AssistantRun(new ScriptedAssistant(), Keyed, NodeCatalog.BuiltIn, new Patch());

        var saved = run.Save([new TranscriptLine(Voice.Failed, $"refused: {Key} is not valid")]);

        saved.Transcript.ShouldHaveSingleItem().Text.ShouldBe("refused: [key] is not valid");
    }

    // --- the happy path -----------------------------------------------------

    [Fact]
    public async Task A_proposal_reaches_the_caller_and_is_kept()
    {
        var built = new Patch();
        built.Nodes.Add(NodeInstance.Create(NodeCatalog.BuiltIn.Require("value"), 0, 0));

        using var run = RunOf(new ScriptedAssistant(
            new PatchEvent.Said("thinking"),
            new PatchEvent.Did("added a knob"),
            new PatchEvent.Proposed(built, "one knob")));

        var events = await Drain(run);

        events.Count.ShouldBe(3);
        run.Proposal.ShouldBe(built);
        run.ProposalSummary.ShouldBe("one knob");
    }

    /// <summary>
    /// Last turn's patch is not this turn's answer.
    /// </summary>
    /// <remarks>
    /// The bug this exists for only appears once a conversation outlives a
    /// proposal, which is what the panel now does. The caller applies whatever
    /// is on the run when a turn ends, so a proposal left standing from an
    /// earlier turn is put on the canvas again — for a message that asked a
    /// question, or asked for nothing at all.
    /// </remarks>
    [Fact]
    public async Task A_proposal_does_not_survive_into_the_next_turn()
    {
        var built = new Patch();
        built.Nodes.Add(NodeInstance.Create(NodeCatalog.BuiltIn.Require("value"), 0, 0));

        using var run = RunOf(ScriptedAssistant.Conversation(
            [new PatchEvent.Proposed(built, "one knob")],
            [new PatchEvent.Said("it is the one above.")]));

        await Drain(run, "build me one");
        run.Proposal.ShouldBe(built);

        // The second turn answers a question and offers nothing.
        await Drain(run, "what did you build?");

        run.Proposal.ShouldBeNull();
        run.ProposalSummary.ShouldBeEmpty();
    }

    /// <summary>
    /// A run has to be usable more than once, or the conversation it holds is
    /// worth nothing: the workbench keeps everything built so far, and that is
    /// what a second message is asked against.
    /// </summary>
    [Fact]
    public async Task A_run_takes_more_than_one_message()
    {
        using var run = RunOf(ScriptedAssistant.Editing());

        await Drain(run, "add a knob");
        await Drain(run, "and another");

        run.Turns.ShouldBe(2);
        run.Workbench.Edits.ShouldBe(2, "the second message edited the patch the first one built");
    }

    /// <summary>
    /// What the run itself put on the canvas is not somebody editing behind it.
    /// Without this the panel reads the applied patch as an outside change and
    /// starts a new conversation, throwing away the one that just succeeded.
    /// </summary>
    [Fact]
    public async Task A_patch_this_run_proposed_and_had_applied_is_not_an_edit_underneath()
    {
        var built = new Patch();
        built.Nodes.Add(NodeInstance.Create(NodeCatalog.BuiltIn.Require("value"), 0, 0));

        using var run = RunOf(new ScriptedAssistant(new PatchEvent.Proposed(built, "one knob")));

        await Drain(run);

        run.Reshaped(built).ShouldBeTrue("nothing has told it the editor took this");

        run.Rebase(built);

        run.Reshaped(built).ShouldBeFalse();
    }

    [Fact]
    public async Task A_conversation_whose_last_request_reached_the_limit_is_spent()
    {
        using var run = RunOf(Sending(50_000), maxContext: 50_000);

        run.Exhausted.ShouldBeFalse();
        await Drain(run);
        run.Exhausted.ShouldBeTrue();
    }

    [Fact]
    public async Task However_many_turns_a_small_conversation_has_it_carries_on()
    {
        using var run = RunOf(Sending(1_000), maxContext: 50_000);

        for (var turn = 0; turn < 20; turn++) await Drain(run);

        run.Turns.ShouldBe(20);
        run.Exhausted.ShouldBeFalse();
    }

    /// <summary>
    /// A limit raised in the settings reaches the conversation already going, so
    /// one that ran out can carry on rather than having to start again.
    /// </summary>
    [Fact]
    public async Task Raising_the_limit_lets_a_conversation_carry_on()
    {
        using var run = RunOf(Sending(50_000), maxContext: 50_000);

        await Drain(run);
        run.Exhausted.ShouldBeTrue();

        run.MaxContext = 100_000;

        run.Exhausted.ShouldBeFalse();
    }

    /// <summary>
    /// The property the whole undo story rests on. The workbench takes a copy,
    /// so whatever was open is still exactly what it was — which is what makes
    /// "put it back" a single assignment in an application that has no undo.
    /// </summary>
    [Fact]
    public async Task The_patch_that_was_open_is_never_touched()
    {
        var open = new Patch();
        open.Nodes.Add(NodeInstance.Create(NodeCatalog.BuiltIn.Require(NodeCatalog.OutputTypeId), 10, 20));

        // This one really edits, through the workbench, the way a real assistant
        // does. An assistant that only talked would prove nothing here.
        using var run = RunOf(ScriptedAssistant.Editing(), open);

        await Drain(run);

        run.Workbench.Edits.ShouldBeGreaterThan(0);
        run.Workbench.Snapshot().Nodes.Count.ShouldBe(2);

        open.Nodes.Count.ShouldBe(1);
        open.Nodes[0].X.ShouldBe(10);
        open.Connections.ShouldBeEmpty();
    }

    // --- when it goes wrong -------------------------------------------------

    [Fact]
    public async Task A_failure_the_assistant_reports_is_just_an_event()
    {
        using var run = RunOf(new ScriptedAssistant(new PatchEvent.Failed("no key")));

        var events = await Drain(run);

        events.OfType<PatchEvent.Failed>().ShouldHaveSingleItem().Message.ShouldBe("no key");
        run.Proposal.ShouldBeNull();
    }

    /// <summary>
    /// A plugin runs in-process with full trust, so one that throws where the
    /// contract says it should not must still cost the turn rather than the
    /// window.
    /// </summary>
    [Fact]
    public async Task An_assistant_that_throws_becomes_a_failure_rather_than_a_crash()
    {
        using var run = RunOf(ScriptedAssistant.Throwing("the wheels came off"));

        var events = await Drain(run);

        events.OfType<PatchEvent.Failed>().ShouldHaveSingleItem()
            .Message.ShouldContain("the wheels came off");
    }

    [Fact]
    public async Task An_assistant_that_throws_before_it_starts_is_survived()
    {
        using var run = RunOf(ScriptedAssistant.RefusingToStart());

        var events = await Drain(run);

        events.OfType<PatchEvent.Failed>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Stopping_ends_the_turn_and_leaves_no_proposal()
    {
        var assistant = new ScriptedAssistant(
            new PatchEvent.Said("one"),
            new PatchEvent.Said("two"),
            new PatchEvent.Proposed(new Patch(), "never got here"));

        using var run = RunOf(assistant);

        var events = new List<PatchEvent>();

        await foreach (var happened in run.Ask("go", TestContext.Current.CancellationToken))
        {
            events.Add(happened);
            run.Stop();
        }

        events.Count.ShouldBe(1);
        run.Proposal.ShouldBeNull();
        run.Running.ShouldBeFalse();
    }

    /// <summary>
    /// Why the panel keeps a flag of its own rather than asking this one.
    /// </summary>
    /// <remarks>
    /// <see cref="AssistantRun.Ask"/> is an async iterator, so
    /// <see cref="AssistantRun.Running"/> is not set until the sequence is first
    /// moved on — which had the shell leaving Ask live and Stop dead for the whole
    /// of every turn. Pinned here because it is a property of the iterator.
    /// </remarks>
    [Fact]
    public async Task A_run_does_not_call_itself_running_until_its_sequence_is_moved_on()
    {
        using var run = RunOf(new ScriptedAssistant(new PatchEvent.Did("a step")));

        var events = run.Ask("go", TestContext.Current.CancellationToken);

        run.Running.ShouldBeFalse();

        await using var walking = events.GetAsyncEnumerator(TestContext.Current.CancellationToken);
        await walking.MoveNextAsync();

        run.Running.ShouldBeTrue();
    }

    [Fact]
    public async Task A_conversation_past_its_limit_takes_no_more_messages()
    {
        using var run = RunOf(Sending(60_000), maxContext: 50_000);

        await Drain(run);
        var second = await Drain(run);

        run.Turns.ShouldBe(1);
        second.ShouldHaveSingleItem().ShouldBeOfType<PatchEvent.Failed>().Message
            .ShouldBe("this conversation has grown to 60,000 tokens, past its limit of 50,000. Start another one.");
    }

    // --- the patch moving underneath ----------------------------------------

    [Fact]
    public void An_untouched_patch_is_seen_as_untouched()
    {
        var open = new Patch();
        using var run = RunOf(new ScriptedAssistant(), open);

        run.Reshaped(open).ShouldBeFalse();
    }

    [Fact]
    public void A_patch_that_gained_a_module_is_noticed()
    {
        var open = new Patch();
        using var run = RunOf(new ScriptedAssistant(), open);

        open.Nodes.Add(NodeInstance.Create(NodeCatalog.BuiltIn.Require("value"), 0, 0));

        run.Reshaped(open).ShouldBeTrue();
    }

    [Fact]
    public void A_patch_that_lost_a_wire_is_noticed()
    {
        var (open, from, into) = TwoValues();
        open.Connections.Add(new Connection(from.Id, 0, into.Id, 0));

        using var run = RunOf(new ScriptedAssistant(), open);

        open.Connections.Clear();

        run.Reshaped(open).ShouldBeTrue();
    }

    [Fact]
    public void A_different_patch_altogether_is_noticed()
    {
        var (open, _, _) = TwoValues();
        using var run = RunOf(new ScriptedAssistant(), open);

        var (other, _, _) = TwoValues();

        run.Reshaped(other).ShouldBeTrue("the same types under other ids are other modules");
    }

    /// <summary>
    /// An undo hands back a new copy of the patch, and a knob turns in place; neither
    /// is a different patch while its modules and wires are the ones there were.
    /// </summary>
    [Fact]
    public void A_patch_with_the_same_modules_and_wires_is_the_same_patch()
    {
        var (open, from, into) = TwoValues();
        open.Connections.Add(new Connection(from.Id, 0, into.Id, 0));

        using var run = RunOf(new ScriptedAssistant(), open);

        from.InputValues[0] = 0.9f;
        into.Name = "Level";

        run.Reshaped(open).ShouldBeFalse();
        run.Reshaped(PatchIO.Read(PatchIO.ToJson(open)).Patch).ShouldBeFalse();
    }

    // --- knobs turned on the canvas -------------------------------------------

    /// <summary>
    /// A knob turned between turns reaches the workbench, and the model hears of it in
    /// one line ahead of the next message rather than in a new conversation.
    /// </summary>
    [Fact]
    public async Task A_knob_turned_between_turns_reaches_the_workbench_and_the_next_message()
    {
        var (open, from, _) = TwoValues();
        var assistant = new ScriptedAssistant(new PatchEvent.Said("ok"));

        using var run = RunOf(assistant, open);

        await Drain(run, "first");

        from.InputValues[0] = 0.25f;
        run.CatchUp(open);

        run.Workbench.Snapshot().Find(from.Id).ShouldNotBeNull().InputValues[0].ShouldBe(0.25f);
        run.Unsaid.ShouldBe("value1.value=0.25");

        await Drain(run, "second");

        assistant.Heard[^1].ShouldStartWith("[Changed on the canvas since your last turn, and already on your workbench: value1.value=0.25.]");
        assistant.Heard[^1].ShouldEndWith("second");
        run.Unsaid.ShouldBeNull("the model has been told");
        run.Turns.ShouldBe(2, "the same conversation");
    }

    [Fact]
    public async Task Nothing_is_told_when_nothing_on_the_canvas_changed()
    {
        var (open, _, _) = TwoValues();
        var assistant = new ScriptedAssistant(new PatchEvent.Said("ok"));

        using var run = RunOf(assistant, open);

        await Drain(run, "first");
        run.CatchUp(open);
        await Drain(run, "second");

        assistant.Heard[^1].ShouldBe("second");
    }

    /// <summary>
    /// The first message opens with the patch, so the model does not spend a request
    /// carrying the whole briefing to ask for it. The next one does not repeat it.
    /// </summary>
    [Fact]
    public async Task The_first_message_opens_with_the_patch_and_the_next_does_not()
    {
        var (open, _, _) = TwoValues();
        var assistant = new ScriptedAssistant(new PatchEvent.Said("ok"));

        using var run = RunOf(assistant, open);

        await Drain(run, "first");
        await Drain(run, "second");

        assistant.Heard[0].ShouldStartWith("[From Flyback, not the person: the patch on your workbench.");
        assistant.Heard[0].ShouldContain("value1");
        assistant.Heard[0].ShouldEndWith("first");
        assistant.Heard[1].ShouldBe("second");
    }

    /// <summary>A first message nobody answered leaves the patch for the next one to carry.</summary>
    [Fact]
    public async Task A_first_message_that_failed_leaves_the_patch_to_the_next()
    {
        var assistant = ScriptedAssistant.Conversation(
            [new PatchEvent.Failed("429: rate limited")],
            [new PatchEvent.Said("ok")]);

        using var run = RunOf(assistant);

        await Drain(run, "first");
        await Drain(run, "second");

        assistant.Heard[1].ShouldStartWith("[From Flyback, not the person: the patch on your workbench.");
        assistant.Heard[1].ShouldEndWith("second");
    }

    /// <summary>
    /// A knob turned while a turn ran survives the proposal, unless the assistant set
    /// the same knob itself: it was asked to change something, and its answer stands.
    /// </summary>
    [Fact]
    public async Task A_knob_turned_while_it_worked_is_kept_in_the_proposal_where_the_assistant_left_it_alone()
    {
        var (open, left, right) = TwoValues();
        var assistant = ScriptedAssistant.Proposing("""{"handle":"value2","knobs":[{"port":"value","value":0.9}]}""");

        using var run = RunOf(assistant, open);

        await Drain(run, "turn the second one up");

        left.InputValues[0] = 0.1f;
        right.InputValues[0] = 0.2f;

        var carried = run.Merge(open);

        var proposed = run.Proposal.ShouldNotBeNull();
        proposed.Find(left.Id).ShouldNotBeNull().InputValues[0].ShouldBe(0.1f);
        proposed.Find(right.Id).ShouldNotBeNull().InputValues[0].ShouldBe(0.9f);

        carried.Count.ShouldBe(2);
        carried.Count(change => change.Kept).ShouldBe(1);

        run.Workbench.Snapshot().Find(left.Id).ShouldNotBeNull().InputValues[0].ShouldBe(0.1f, "the next proposal keeps it too");
        run.Unsaid.ShouldBe("value1.value=0.1");
    }

    /// <summary>
    /// A patch saved with knobs the workbench never saw carries them in when its
    /// conversation is carried on.
    /// </summary>
    [Fact]
    public async Task A_conversation_carried_on_takes_the_knobs_the_patch_was_saved_with()
    {
        var (open, from, _) = TwoValues();
        var assistant = new ScriptedAssistant(new PatchEvent.Said("ok"));
        SavedConversation saved;

        using (var first = RunOf(assistant, open))
        {
            await Drain(first);
            saved = first.Save([]);
        }

        from.InputValues[0] = 0.75f;

        using var carried = new AssistantRun(
            assistant, AssistantConfig.Unset, NodeCatalog.BuiltIn, open, resuming: saved);

        carried.Workbench.Snapshot().Find(from.Id).ShouldNotBeNull().InputValues[0].ShouldBe(0.75f);
        carried.Unsaid.ShouldBe("value1.value=0.75");
    }

    /// <summary>
    /// A turn that failed before anything came back is not one of the
    /// conversation's turns, and what it was to tell about the canvas waits for
    /// the next.
    /// </summary>
    [Fact]
    public async Task A_turn_that_failed_before_anything_came_back_is_not_counted()
    {
        var (open, from, _) = TwoValues();

        using var run = RunOf(ScriptedAssistant.RefusingToStart(), open);

        from.InputValues[0] = 0.25f;
        run.CatchUp(open);

        var events = await Drain(run);

        events.ShouldHaveSingleItem().ShouldBeOfType<PatchEvent.Failed>();
        run.Turns.ShouldBe(0);
        run.Unsaid.ShouldBe("value1.value=0.25");
    }

    /// <summary>A wait for a rate limit is the host talking, so a turn that waited and was then refused is not counted either.</summary>
    [Fact]
    public async Task A_turn_that_only_waited_before_it_failed_is_not_counted()
    {
        using var run = RunOf(new ScriptedAssistant(TurnLoop.Wait(TimeSpan.FromSeconds(31), 429), new PatchEvent.Failed("quota")));

        var events = await Drain(run);

        events.Count.ShouldBe(2);
        run.Turns.ShouldBe(0);
    }

    /// <summary>
    /// A knob the assistant set and never proposed is its own, not something
    /// changed on the canvas, so carrying the conversation on over the same
    /// canvas neither undoes it nor tells the model otherwise.
    /// </summary>
    [Fact]
    public async Task A_conversation_carried_on_keeps_a_knob_it_set_and_never_offered()
    {
        var (open, _, second) = TwoValues();
        var assistant = ScriptedAssistant.Proposing("""{"handle":"value2","knobs":[{"port":"value","value":0.9}]}""");
        SavedConversation saved;

        using (var first = RunOf(assistant, open))
        {
            await Drain(first);
            saved = first.Save([]);
        }

        using var carried = new AssistantRun(
            assistant, AssistantConfig.Unset, NodeCatalog.BuiltIn, open, resuming: saved);

        carried.Workbench.Snapshot().Find(second.Id).ShouldNotBeNull().InputValues[0].ShouldBe(0.9f);
        carried.Unsaid.ShouldBeNull();
    }

    /// <summary>
    /// A conversation's turn is run here, so what a turn promises holds even for a
    /// provider that answers <c>Ask</c> some other way.
    /// </summary>
    [Fact]
    public async Task The_host_runs_the_turn_of_a_conversation_whatever_its_own_ask_says()
    {
        using var run = RunOf(new Conversing());

        var events = await Drain(run);

        events.OfType<PatchEvent.Said>().ShouldHaveSingleItem().Text.ShouldBe("from the loop");
        events.OfType<PatchEvent.Failed>().ShouldBeEmpty();
    }

    private static (Patch Patch, NodeInstance First, NodeInstance Second) TwoValues()
    {
        var patch = new Patch();
        var first = NodeInstance.Create(NodeCatalog.BuiltIn.Require("value"), 0, 0);
        var second = NodeInstance.Create(NodeCatalog.BuiltIn.Require("value"), 0, 0);

        patch.Nodes.Add(first);
        patch.Nodes.Add(second);
        patch.EnsureOutput();

        return (patch, first, second);
    }

    // --- carried on from a saved one ------------------------------------------

    /// <summary>
    /// What was built, how far the conversation got and what the provider said it
    /// was, all back — over the patch that has just been opened, which is what an
    /// edit underneath is noticed against from here on.
    /// </summary>
    [Fact]
    public async Task A_conversation_saved_and_carried_on_keeps_what_it_built_and_how_far_it_got()
    {
        var assistant = ScriptedAssistant.Editing();
        SavedConversation saved;

        using (var first = RunOf(assistant))
        {
            await Drain(first, "add a knob");
            saved = first.Save([new TranscriptLine(Voice.You, "add a knob")]);
        }

        saved.History.ShouldBe(ScriptedAssistant.Remembered);
        saved.Transcript.ShouldHaveSingleItem().Text.ShouldBe("add a knob");

        var opened = new Patch();

        using var carried = new AssistantRun(
            assistant, AssistantConfig.Unset, NodeCatalog.BuiltIn, opened, resuming: saved);

        carried.PickedUp.ShouldBeTrue();
        assistant.Given.ShouldBe(ScriptedAssistant.Remembered);
        carried.Turns.ShouldBe(1);
        carried.Workbench.Snapshot().Nodes.Count.ShouldBe(2, "the knob it built, and the Output");
        carried.Reshaped(opened).ShouldBeFalse();
    }

    /// <summary>
    /// A provider that cannot take the conversation back costs the model its memory
    /// and nothing else: the patch it was building is still on the bench.
    /// </summary>
    [Fact]
    public async Task A_provider_that_cannot_carry_it_on_starts_again_over_what_was_built()
    {
        SavedConversation saved;

        using (var first = RunOf(ScriptedAssistant.Editing()))
        {
            await Drain(first);
            saved = first.Save([]);
        }

        using var carried = new AssistantRun(
            new ScriptedAssistant { Forgets = true },
            AssistantConfig.Unset,
            NodeCatalog.BuiltIn,
            new Patch(),
            resuming: saved);

        carried.PickedUp.ShouldBeFalse();
        carried.Workbench.Snapshot().Nodes.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_conversation_carried_on_remembers_how_large_it_had_grown()
    {
        SavedConversation saved;

        using (var first = RunOf(Sending(60_000), maxContext: 100_000))
        {
            await Drain(first);
            first.Exhausted.ShouldBeFalse();
            saved = SavedConversation.Read(first.Save([]).ToJson()).ShouldNotBeNull();
        }

        saved.Unresumable(50_000, null, SettingValues.None).ShouldBe(AssistantRun.Grown);

        using var carried = new AssistantRun(
            Sending(60_000),
            AssistantConfig.Unset,
            NodeCatalog.BuiltIn,
            new Patch(),
            maxContext: 50_000,
            resuming: saved);

        carried.Exhausted.ShouldBeTrue();
    }

    /// <summary>
    /// A saved workbench that will not read as a patch is a conversation that starts
    /// again over the patch on the canvas, rather than one that cannot start at all.
    /// </summary>
    [Fact]
    public void A_saved_workbench_that_will_not_read_starts_again_over_the_patch_on_the_canvas()
    {
        var saved = new SavedConversation(
            "scripted",
            string.Empty,
            3,
            new WorkbenchState("not a patch", "not a patch either", new Dictionary<string, Guid>(), 0, 0),
            ScriptedAssistant.Remembered,
            []);

        var opened = new Patch();

        using var carried = new AssistantRun(
            new ScriptedAssistant(), AssistantConfig.Unset, NodeCatalog.BuiltIn, opened, resuming: saved);

        carried.PickedUp.ShouldBeFalse();
        carried.Turns.ShouldBe(0);
        carried.Reshaped(opened).ShouldBeFalse();
    }

    [Fact]
    public void A_saved_workbench_whose_patch_has_no_module_list_still_opens()
    {
        var saved = new SavedConversation(
            "scripted",
            string.Empty,
            1,
            new WorkbenchState("""{"Nodes":null}""", """{"Nodes":null}""", new Dictionary<string, Guid>(), 0, 0),
            ScriptedAssistant.Remembered,
            []);

        var carried = Should.NotThrow(() =>
            new AssistantRun(new ScriptedAssistant(), AssistantConfig.Unset, NodeCatalog.BuiltIn, new Patch(), resuming: saved));

        using (carried)
            carried.Workbench.Snapshot().Output.ShouldNotBeNull();
    }

    // --- what it cost ---------------------------------------------------------

    [Fact]
    public async Task The_tokens_each_request_reports_add_up_across_turns()
    {
        using var run = RunOf(new ScriptedAssistant(new PatchEvent.Cost(100, 80, 10), new PatchEvent.Cost(50, 0, 5)));

        await Drain(run);
        await Drain(run);

        run.Tokens.ShouldBe(new TokensSpent(4, 300, 160, 30, Context: 50));
    }

    [Fact]
    public async Task What_a_conversation_cost_is_kept_when_it_is_saved_and_carried_on()
    {
        using var first = RunOf(new ScriptedAssistant(new PatchEvent.Cost(100, 80, 10)));

        await Drain(first);

        var saved = SavedConversation.Read(first.Save([]).ToJson()).ShouldNotBeNull();

        using var carried = new AssistantRun(
            new ScriptedAssistant(new PatchEvent.Cost(7, 0, 3)),
            AssistantConfig.Unset,
            NodeCatalog.BuiltIn,
            new Patch(),
            resuming: saved);

        carried.Tokens.ShouldBe(new TokensSpent(1, 100, 80, 10, Context: 100));

        await Drain(carried);

        carried.Tokens.ShouldBe(new TokensSpent(2, 107, 80, 13, Context: 7));
    }

    /// <summary>A conversation saved before the cost was kept opens with none counted.</summary>
    [Fact]
    public void A_conversation_saved_without_its_cost_reads_as_having_none()
    {
        var saved = new SavedConversation(
            "scripted",
            string.Empty,
            2,
            new WorkbenchState("""{"nodes":[]}""", """{"nodes":[]}""", new Dictionary<string, Guid>(), 0, 0),
            null,
            []);

        var read = SavedConversation.Read(saved.ToJson()).ShouldNotBeNull();

        read.Tokens.ShouldBeNull();

        using var carried = new AssistantRun(
            new ScriptedAssistant(), AssistantConfig.Unset, NodeCatalog.BuiltIn, new Patch(), resuming: read);

        carried.Tokens.None.ShouldBeTrue();
    }

    [Theory]
    [InlineData(1, 0, 0, 0, 0, "1 turn · 0 in (0 cached) · 0 out · 0 of 100k context")]
    [InlineData(3, 87_040, 80_000, 3_100, 36_200, "3 turns · 87k in (80k cached) · 3.1k out · 36.2k of 100k context")]
    [InlineData(12, 1_250_000, 0, 999, 99_000, "12 turns · 1.25M in (0 cached) · 999 out · 99k of 100k context")]
    public void The_footer_gives_counts_in_thousands(int turns, int input, int cached, int output, int context, string told) =>
        new TokensSpent(1, input, cached, output, context).Told(turns, 100_000).ShouldBe(told);

    // --- the fake -----------------------------------------------------------

    /// <summary>
    /// A provider whose sessions are conversations, and which answers <c>Ask</c> with
    /// a loop of its own that the host must never reach.
    /// </summary>
    private sealed class Conversing : IPatchAssistant
    {
        public string Id => "scripted";

        public string Name => "Conversing";

        public int Priority => 0;

        public AssistantSchema Schema { get; } =
            new("scripted", [new AssistantModel("scripted")], "NONE", "none needed");

        public AssistantCredential Credential => Schema.Credential;

        public IReadOnlyList<SettingField> Form(SettingValues values) => Schema.Form(values);

        public AssistantSenses Senses(SettingValues values) => Schema.Senses(values);

        public string? Unavailable(AssistantConfig config) => null;

        public Uri? Endpoint(SettingValues values) => new("https://assistant.test/");

        public IPatchSession Start(PatchWorkbench workbench, AssistantConfig config) => new Session(workbench);

        private sealed class Session(PatchWorkbench workbench) : IModelConversation
        {
            public PatchWorkbench Workbench => workbench;

            public bool HearsItself => false;

            public string? EarModel => null;

            public void Forget()
            {
            }

            public void Add(string instruction)
            {
            }

            public Task<ModelReply> Send(CancellationToken cancel) => Task.FromResult(new ModelReply("from the loop", []));

            public void Add(IReadOnlyList<ToolAnswer> answers)
            {
            }

            public Task<string?> Listen(string model, string briefing, byte[] wav, CancellationToken cancel) =>
                Task.FromResult<string?>(null);

            async IAsyncEnumerable<PatchEvent> IPatchSession.Ask(string instruction, [EnumeratorCancellation] CancellationToken cancel)
            {
                await Task.Yield();
                yield return new PatchEvent.Failed("its own loop");
            }

            public void Dispose()
            {
            }
        }
    }

    private sealed class ScriptedAssistant(params PatchEvent[] script) : IPatchAssistant, IPatchSession
    {
        /// <summary>What every one of these says the conversation was, when asked to save it.</summary>
        public const string Remembered = "what was said";

        private PatchWorkbench? bench;
        private string? throwsAfterStarting;
        private bool refusesToStart;
        private bool edits;
        private string? proposes;
        private Queue<PatchEvent[]>? turns;

        /// <summary>Whether this one cannot take a saved conversation back.</summary>
        public bool Forgets { get; init; }

        /// <summary>What <see cref="Resume"/> was handed, or null where it never was.</summary>
        public string? Given { get; private set; }

        /// <summary>Every message it was sent, as it arrived.</summary>
        public List<string> Heard { get; } = [];

        public IPatchSession? Resume(PatchWorkbench workbench, AssistantConfig config, string saved)
        {
            if (Forgets) return null;

            bench = workbench;
            Given = saved;

            return this;
        }

        public string? Save() => Remembered;

        public static ScriptedAssistant Throwing(string message) =>
            new() { throwsAfterStarting = message };

        public static ScriptedAssistant RefusingToStart() => new() { refusesToStart = true };

        /// <summary>One that actually builds something, so a copy can be told from the original.</summary>
        public static ScriptedAssistant Editing() => new() { edits = true };

        /// <summary>One that sets knobs with these set_knobs arguments, then proposes what it has.</summary>
        public static ScriptedAssistant Proposing(string knobs) => new() { proposes = knobs };

        /// <summary>
        /// One with something different to say each time it is asked, which is
        /// what a conversation is. The flat script replays per turn, which is
        /// what most of these want and what a multi-turn test must not have.
        /// </summary>
        public static ScriptedAssistant Conversation(params PatchEvent[][] turns) =>
            new() { turns = new Queue<PatchEvent[]>(turns) };

        public string Id => "scripted";

        public string Name => "Scripted";

        public int Priority => 0;

        public AssistantSchema Schema { get; } =
            new("scripted", [new AssistantModel("scripted")], "NONE", "none needed");

        public AssistantCredential Credential => Schema.Credential;

        public Uri? Endpoint(SettingValues values) => new("https://assistant.test/");

        public IReadOnlyList<SettingField> Form(SettingValues values) => Schema.Form(values);

        public AssistantSenses Senses(SettingValues values) => Schema.Senses(values);

        public string? Unavailable(AssistantConfig config) => null;

        public IPatchSession Start(PatchWorkbench workbench, AssistantConfig config)
        {
            bench = workbench;
            return this;
        }

        public async IAsyncEnumerable<PatchEvent> Ask(
            string instruction,
            [EnumeratorCancellation] CancellationToken cancel)
        {
            Heard.Add(instruction);

            if (refusesToStart) throw new InvalidOperationException("would not start");

            if (proposes is not null && bench is not null)
            {
                await bench.InvokeAsync("set_knobs", JsonSerializer.Deserialize<JsonElement>(proposes), cancel);

                yield return new PatchEvent.Proposed(bench.Snapshot(), "knobs set");
            }

            if (edits && bench is not null)
            {
                var added = await bench.InvokeAsync(
                    "add_module",
                    // No handle asked for: the workbench names each one, so this
                    // can be told to build twice without the second refusing
                    // over a name the first took.
                    JsonSerializer.Deserialize<JsonElement>("""{"type_id":"value"}"""),
                    cancel);

                yield return new PatchEvent.Did(added.Text);
            }

            var saying = turns is null
                ? script
                : turns.Count > 0 ? turns.Dequeue() : [];

            foreach (var happened in saying)
            {
                cancel.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return happened;
            }

            if (throwsAfterStarting is { } message) throw new InvalidOperationException(message);
        }

        public void Dispose()
        {
        }
    }
}
