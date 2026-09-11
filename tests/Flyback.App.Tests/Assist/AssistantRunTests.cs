using System.Runtime.CompilerServices;
using System.Text.Json;
using Flyback.App.Assist;
using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Shouldly;
using Xunit;

namespace Flyback.App.Tests.Assist;

/// <summary>
/// The shell's half of a conversation, driven by a fake on the far side so the
/// test decides the timing. The same arrangement <c>LoopbackDevice</c> gives
/// <see cref="Flyback.App.Audio.AudioEngine"/>: no network, no Avalonia, no
/// waiting.
/// </summary>
public class AssistantRunTests
{
    private static AssistantRun RunOf(ScriptedAssistant assistant, Patch? start = null, int maxTurns = 12) =>
        new(assistant,
            AssistantConfig.Unset,
            NodeCatalog.BuiltIn,
            start ?? new Patch(),
            maxTurns);

    private static async Task<List<PatchEvent>> Drain(AssistantRun run, string instruction = "make something")
    {
        var events = new List<PatchEvent>();

        await foreach (var happened in run.Ask(instruction, TestContext.Current.CancellationToken))
            events.Add(happened);

        return events;
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

        run.EditedUnderneath(built).ShouldBeTrue("nothing has told it the editor took this");

        run.Rebase(built);

        run.EditedUnderneath(built).ShouldBeFalse();
    }

    [Fact]
    public async Task A_conversation_that_has_had_its_turns_says_so()
    {
        using var run = RunOf(new ScriptedAssistant(new PatchEvent.Said("hello")), maxTurns: 1);

        run.Exhausted.ShouldBeFalse();
        await Drain(run);
        run.Exhausted.ShouldBeTrue();
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

        run.Before.ShouldBeSameAs(open);
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
    public async Task A_conversation_runs_out_of_turns_rather_than_running_forever()
    {
        using var run = RunOf(new ScriptedAssistant(new PatchEvent.Did("a step")), maxTurns: 2);

        await Drain(run);
        await Drain(run);
        var third = await Drain(run);

        run.Turns.ShouldBe(2);
        third.OfType<PatchEvent.Failed>().ShouldHaveSingleItem().Message.ShouldContain("turns");
    }

    // --- the patch moving underneath ----------------------------------------

    [Fact]
    public void An_untouched_patch_is_seen_as_untouched()
    {
        var open = new Patch();
        using var run = RunOf(new ScriptedAssistant(), open);

        run.EditedUnderneath(open).ShouldBeFalse();
    }

    [Fact]
    public void A_patch_that_gained_a_module_is_noticed()
    {
        var open = new Patch();
        using var run = RunOf(new ScriptedAssistant(), open);

        open.Nodes.Add(NodeInstance.Create(NodeCatalog.BuiltIn.Require("value"), 0, 0));

        run.EditedUnderneath(open).ShouldBeTrue();
    }

    [Fact]
    public void A_different_patch_altogether_is_noticed()
    {
        using var run = RunOf(new ScriptedAssistant(), new Patch());

        run.EditedUnderneath(new Patch()).ShouldBeTrue();
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
        carried.Before.ShouldBeSameAs(opened);
        carried.EditedUnderneath(opened).ShouldBeFalse();
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
    public async Task A_conversation_carried_on_still_runs_out_of_turns()
    {
        SavedConversation saved;

        using (var first = RunOf(new ScriptedAssistant(new PatchEvent.Said("hello")), maxTurns: 2))
        {
            await Drain(first);
            saved = first.Save([]);
        }

        using var carried = new AssistantRun(
            new ScriptedAssistant(new PatchEvent.Said("hello")),
            AssistantConfig.Unset,
            NodeCatalog.BuiltIn,
            new Patch(),
            maxTurns: 2,
            resuming: saved);

        await Drain(carried);

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
        carried.Before.ShouldBeSameAs(opened);
    }

    // --- the fake -----------------------------------------------------------

    private sealed class ScriptedAssistant(params PatchEvent[] script) : IPatchAssistant, IPatchSession
    {
        /// <summary>What every one of these says the conversation was, when asked to save it.</summary>
        public const string Remembered = "what was said";

        private PatchWorkbench? bench;
        private string? throwsAfterStarting;
        private bool refusesToStart;
        private bool edits;
        private Queue<PatchEvent[]>? turns;

        /// <summary>Whether this one cannot take a saved conversation back.</summary>
        public bool Forgets { get; init; }

        /// <summary>What <see cref="Resume"/> was handed, or null where it never was.</summary>
        public string? Given { get; private set; }

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

        public IReadOnlyList<AssistantField> Form(AssistantValues values) => Schema.Form(values);

        public AssistantSenses Senses(AssistantValues values) => Schema.Senses(values);

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
            if (refusesToStart) throw new InvalidOperationException("would not start");

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
