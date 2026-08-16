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
            new AssistantConfig(string.Empty, "scripted"),
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
    /// The property the whole undo story rests on. The workbench takes a copy,
    /// so whatever was open is still exactly what it was — which is what makes
    /// "put it back" a single assignment in an application that has no undo.
    /// </summary>
    [Fact]
    public async Task The_patch_that_was_open_is_never_touched()
    {
        var open = new Patch();
        open.Nodes.Add(NodeInstance.Create(NodeCatalog.BuiltIn.Require("video.output"), 10, 20));

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

    // --- the fake -----------------------------------------------------------

    private sealed class ScriptedAssistant(params PatchEvent[] script) : IPatchAssistant, IPatchSession
    {
        private PatchWorkbench? bench;
        private string? throwsAfterStarting;
        private bool refusesToStart;
        private bool edits;

        public static ScriptedAssistant Throwing(string message) =>
            new() { throwsAfterStarting = message };

        public static ScriptedAssistant RefusingToStart() => new() { refusesToStart = true };

        /// <summary>One that actually builds something, so a copy can be told from the original.</summary>
        public static ScriptedAssistant Editing() => new() { edits = true };

        public string Id => "scripted";

        public string Name => "Scripted";

        public int Priority => 0;

        public AssistantSchema Schema { get; } = new("scripted", ["scripted"], "NONE", "none needed");

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
                    JsonSerializer.Deserialize<JsonElement>("""{"type_id":"value","handle":"knob1"}"""),
                    cancel);

                yield return new PatchEvent.Did(added.Text);
            }

            foreach (var happened in script)
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
