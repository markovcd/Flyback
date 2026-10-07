using System.Runtime.CompilerServices;
using Flyback.Assist;
using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Decide;
using Flyback.Plugins.Settings;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Tests.Assist;

/// <summary>A message read by the decision model before the assistant is sent it, and the proposal checked once it comes back.</summary>
public sealed class TurnReadingTests
{
    private readonly Transcript transcript = new();
    private readonly Echoing assistant = new();

    private AssistantSession Begun(Reader reader)
    {
        var decisions = new Decisions([reader], new DecisionSettings(), new Credentials(null), new ModelStore(null));
        var session = new AssistantSession(transcript, decisions: decisions);

        session.Begin(new AssistantRun(assistant, AssistantConfig.Unset, NodeCatalog.BuiltIn, new Patch(), AssistantRun.ContextLimit), assistant, AssistantConfig.Unset, new AssistantSettings(), resumed: false, null);

        return session;
    }

    private static async Task Drain(AssistantSession session, string message)
    {
        await foreach (var _ in session.Ask(message, TestContext.Current.CancellationToken))
        {
        }
    }

    [Fact]
    public async Task A_question_is_said_to_be_read_as_one_and_answered_rather_than_built()
    {
        using var session = Begun(new Reader("question", 0.8));

        await Drain(session, "why is it so quiet?");

        transcript.Lines.ShouldContain(new TranscriptLine(Voice.You, "why is it so quiet?"));
        transcript.Said.ShouldContain(s => s.Kind == "intent" && s.Text == "Read as a question about the patch (0.80).");
        assistant.Instructions.ShouldHaveSingleItem().ShouldStartWith(TurnReading.Answering);
    }

    [Fact]
    public async Task An_edit_is_sent_as_it_was_typed()
    {
        using var session = Begun(new Reader("edit", 0.9));

        await Drain(session, "make it slower");

        assistant.Instructions.ShouldHaveSingleItem().ShouldNotContain(TurnReading.Answering);
    }

    [Fact]
    public async Task A_message_surely_about_something_else_is_held_back_once_and_sent_the_second_time()
    {
        using var session = Begun(new Reader("other", 0.95));

        await Drain(session, "what is the capital of France?");

        assistant.Instructions.ShouldBeEmpty();
        transcript.Lines[^1].Text.ShouldContain("Send it again to send it anyway");

        await Drain(session, "what is the capital of France?");

        assistant.Instructions.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_proposal_the_model_doubts_is_said_to_maybe_not_be_what_was_asked()
    {
        assistant.Proposing = true;
        using var session = Begun(new Reader("edit", 0.9) { Does = 0.2 });

        await Drain(session, "make it blue");

        transcript.Said.ShouldContain(s => s.Kind == "doubt" && s.Text.Contains("may not be what was asked for (0.20"));
    }

    [Fact]
    public async Task A_proposal_the_model_believes_is_said_nothing_about()
    {
        assistant.Proposing = true;
        using var session = Begun(new Reader("edit", 0.9) { Does = 0.9 });

        await Drain(session, "make it blue");

        transcript.Said.ShouldNotContain(s => s.Kind == "doubt");
    }

    [Fact]
    public async Task Without_a_model_nothing_is_read_and_the_message_goes_as_typed()
    {
        var session = new AssistantSession(transcript, decisions: Decisions.None);
        session.Begin(new AssistantRun(assistant, AssistantConfig.Unset, NodeCatalog.BuiltIn, new Patch(), AssistantRun.ContextLimit), assistant, AssistantConfig.Unset, new AssistantSettings(), resumed: false, null);

        await Drain(session, "why is it so quiet?");

        transcript.Said.ShouldNotContain(s => s.Kind == "intent");
        assistant.Instructions.ShouldHaveSingleItem().ShouldNotContain(TurnReading.Answering);
        session.Dispose();
    }

    private sealed class Transcript : ITranscript
    {
        private readonly List<TranscriptLine> lines = [];

        public List<Spoken> Said { get; } = [];

        public IReadOnlyList<TranscriptLine> Lines => lines;

        public bool IsEmpty => lines.Count == 0;

        public void Clear()
        {
            lines.Clear();
            Said.Clear();
        }

        public void Put(Spoken spoken)
        {
            Said.Add(spoken);

            if (spoken.Keep) lines.Add(spoken.Line);
        }
    }

    /// <summary>Reads every message as one intent, and believes every proposal to a set degree.</summary>
    private sealed class Reader(string intent, double sure) : IDecisionModel
    {
        public double Does { get; init; } = 1;

        public string Id => "reader";

        public string Name => "Reader";

        public int Priority => 0;

        public AssistantCredential? Credential => null;

        public IReadOnlyList<SettingField> Form(SettingValues values) => [];

        public string? Unavailable(DecisionConfig config) => null;

        public Task<Decision> DecideAsync(DecisionRequest request, DecisionConfig config, CancellationToken cancel) =>
            Task.FromResult(new Decision("reader", request.Questions.ToDictionary(q => q.Key, q => q.Value switch
            {
                Question.Choice choice => (Answer)new Answer.Chosen(intent, choice.Options.ToDictionary(o => o.Label, o => o.Label == intent ? sure : (1 - sure) / (choice.Options.Count - 1)), sure),
                _ => new Answer.YesNo(Does),
            }), DecisionUsage.None));
    }

    /// <summary>Keeps each instruction it was sent, and proposes the patch it was handed when asked to.</summary>
    private sealed class Echoing : IPatchAssistant
    {
        public List<string> Instructions { get; } = [];

        public bool Proposing { get; set; }

        public string Id => "echoing";

        public string Name => "Echoing";

        public int Priority => 0;

        public AssistantCredential Credential => new("FLYBACK_ECHOING_KEY", "No key is needed.");

        public Uri? Endpoint(SettingValues values) => new("https://assistant.test/");

        public IReadOnlyList<SettingField> Form(SettingValues values) => [];

        public AssistantSenses Senses(SettingValues values) => new(false);

        public string? Unavailable(AssistantConfig config) => null;

        public IPatchSession Start(PatchWorkbench workbench, AssistantConfig config) => new Session(this, workbench);

        private sealed class Session(Echoing owner, PatchWorkbench workbench) : IPatchSession
        {
            public async IAsyncEnumerable<PatchEvent> Ask(string instruction, [EnumeratorCancellation] CancellationToken cancel)
            {
                await Task.Yield();

                // The first turn carries the patch ahead of what was typed.
                owner.Instructions.Add(instruction[(instruction.IndexOf(Environment.NewLine + Environment.NewLine, StringComparison.Ordinal) is var at and >= 0 ? at + 2 * Environment.NewLine.Length : 0)..]);

                yield return new PatchEvent.Said("done");

                if (owner.Proposing) yield return new PatchEvent.Proposed(workbench.Snapshot(), "a blue field");
            }

            public void Dispose()
            {
            }
        }
    }
}
