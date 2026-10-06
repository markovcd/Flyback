using System.Runtime.CompilerServices;
using System.Text.Json;
using Flyback.Assist;
using Flyback.Core.Graph;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Settings;
using Shouldly;
using Xunit;

namespace Flyback.Editor.Tests.Assist;

/// <summary>
/// What the editor's column and <c>flyback-cli ask</c> both tell their transcript
/// when a conversation begins, carries on and takes a turn.
/// </summary>
public sealed class AssistantSessionTests
{
    private readonly Transcript transcript = new();
    private readonly Talking assistant = new();

    [Fact]
    public void A_new_conversation_over_one_on_show_clears_it_and_says_why()
    {
        transcript.Put(new Spoken(new TranscriptLine(Voice.Said, "from before"), "said", "from before"));

        using var session = new AssistantSession(transcript);

        session.Begin(Run(), assistant, AssistantConfig.Unset, new AssistantSettings(), resumed: false, "Because.");

        transcript.Lines.ShouldBe([new TranscriptLine(Voice.Note, "Because.")]);
        transcript.Said.ShouldContain(spoken => spoken.Kind == "briefing" && !spoken.Keep);
    }

    [Fact]
    public void A_new_conversation_over_nothing_says_nothing_about_why()
    {
        using var session = new AssistantSession(transcript);

        session.Begin(Run(), assistant, AssistantConfig.Unset, new AssistantSettings(), resumed: false, "Because.");

        transcript.Lines.ShouldBeEmpty();
    }

    [Fact]
    public void A_carried_on_conversation_the_provider_forgot_says_so_and_keeps_what_is_shown()
    {
        transcript.Put(new Spoken(new TranscriptLine(Voice.Said, "from before"), "said", "from before"));

        var saved = Saved();
        using var session = new AssistantSession(transcript);

        session.Begin(Run(saved), assistant, AssistantConfig.Unset, new AssistantSettings(), resumed: true, null);

        transcript.Lines[0].Text.ShouldBe("from before");
        transcript.Lines[^1].Text.ShouldContain("could not pick up what was said before");
        transcript.Said.ShouldNotContain(spoken => spoken.Kind == "briefing");
    }

    [Fact]
    public async Task A_turn_writes_the_message_and_everything_that_happened()
    {
        using var session = Begun();

        await foreach (var _ in session.Ask("hello", TestContext.Current.CancellationToken))
        {
        }

        transcript.Said.Select(spoken => spoken.Kind).ShouldBe(["briefing", "you", "said", "cost"]);
        transcript.Lines[0].ShouldBe(new TranscriptLine(Voice.You, "hello"));
    }

    [Fact]
    public async Task What_arrives_after_the_conversation_ended_is_not_written()
    {
        using var session = Begun();

        await foreach (var happened in session.Ask("hello", TestContext.Current.CancellationToken))
            session.End();

        transcript.Said.Select(spoken => spoken.Kind).ShouldBe(["briefing", "you", "said"]);
    }

    [Fact]
    public async Task A_conversation_that_grew_past_its_context_limit_is_spent()
    {
        using var session = new AssistantSession(transcript);

        session.Begin(Run(maxContext: 1), assistant, AssistantConfig.Unset, new AssistantSettings(), resumed: false, null);

        session.Spent(assistant, AssistantConfig.Unset).ShouldBeNull();

        await foreach (var _ in session.Ask("hello", TestContext.Current.CancellationToken))
        {
        }

        session.Spent(assistant, AssistantConfig.Unset).ShouldNotBeNull().ShouldBe(AssistantRun.Grown);
    }

    [Fact]
    public void A_conversation_asked_with_other_settings_is_spent()
    {
        using var session = Begun();

        var other = new AssistantConfig(AssistantConfig.Unset.Transport, new SettingValues([new("model", "other")]));

        session.Spent(assistant, other).ShouldNotBeNull().ShouldContain("settings changed");
    }

    [Fact]
    public async Task Saving_carries_the_kept_lines_and_not_the_briefing()
    {
        using var session = Begun();

        await foreach (var _ in session.Ask("hello", TestContext.Current.CancellationToken))
        {
        }

        var saved = session.Save();

        saved.Transcript.ShouldContain(new TranscriptLine(Voice.You, "hello"));
        saved.Transcript.ShouldNotContain(line => line.Voice == Voice.Briefing);
    }

    private AssistantSession Begun()
    {
        var session = new AssistantSession(transcript);

        session.Begin(Run(), assistant, AssistantConfig.Unset, new AssistantSettings(), resumed: false, null);

        return session;
    }

    private AssistantRun Run(SavedConversation? resuming = null, int maxContext = AssistantRun.ContextLimit) =>
        new(assistant, AssistantConfig.Unset, NodeCatalog.BuiltIn, new Patch(), maxContext, resuming: resuming);

    /// <summary>A conversation saved by another run, which <see cref="Talking"/> cannot take back.</summary>
    private SavedConversation Saved()
    {
        using var earlier = Run();

        return SavedConversation.Read(earlier.Save([new TranscriptLine(Voice.Said, "from before")]).ToJson()).ShouldNotBeNull();
    }

    /// <summary>Kept in memory, with everything it was handed in order.</summary>
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

    /// <summary>Says one thing a turn and remembers nothing.</summary>
    private sealed class Talking : IPatchAssistant
    {
        public string Id => "talking";

        public string Name => "Talking";

        public int Priority => 0;

        public AssistantCredential Credential => new("FLYBACK_TALKING_KEY", "No key is needed.");

        public Uri? Endpoint(SettingValues values) => new("https://assistant.test/");

        public IReadOnlyList<SettingField> Form(SettingValues values) => [];

        public AssistantSenses Senses(SettingValues values) => new(false);

        public string? Unavailable(AssistantConfig config) => null;

        public IPatchSession Start(PatchWorkbench workbench, AssistantConfig config) => new Session();

        private sealed class Session : IPatchSession
        {
            public async IAsyncEnumerable<PatchEvent> Ask(string instruction, [EnumeratorCancellation] CancellationToken cancel)
            {
                await Task.Yield();

                yield return new PatchEvent.Said(JsonSerializer.Serialize(instruction));
                yield return new PatchEvent.Cost(1, 0, 1);
            }

            public void Dispose()
            {
            }
        }
    }
}
