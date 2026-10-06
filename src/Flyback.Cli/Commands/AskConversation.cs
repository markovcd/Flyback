using System.Diagnostics;
using System.Globalization;
using Flyback.Assist;
using Flyback.Cli.Common;
using Flyback.Cli.Models;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Flyback.Plugins.Assist;

namespace Flyback.Cli.Commands;

/// <summary>
/// One conversation driven from the console: the editor's <see cref="AssistantSession"/>
/// with a console where the panel has a transcript, and a file where it has a canvas.
/// </summary>
internal sealed class AskConversation : IDisposable
{
    private readonly IPatchAssistant assistant;
    private readonly AssistantConfig config;
    private readonly AssistantSettings settings;
    private readonly string? settingsPath;
    private readonly ModuleCatalog modules;
    private readonly IReadOnlyList<PatchPreset> presets;
    private readonly AskedPatch about;
    private readonly ConversationStore store;
    private readonly TextWriter error;
    private readonly int limit;

    private readonly ConsoleTranscript transcript;
    private readonly AssistantSession session;

    private Patch current;

    public AskConversation(
        IPatchAssistant assistant,
        AssistantConfig config,
        AssistantSettings settings,
        string? settingsPath,
        ModuleCatalog modules,
        IReadOnlyList<PatchPreset> presets,
        AskedPatch about,
        AskOptions options,
        ConversationStore store,
        string? logFolder,
        TextWriter output,
        TextWriter error)
    {
        this.assistant = assistant;
        this.config = config;
        this.settings = settings;
        this.settingsPath = settingsPath;
        this.modules = modules;
        this.presets = presets;
        this.about = about;
        this.store = store;
        this.error = error;

        limit = options.Context ?? settings.ContextLimit;
        current = about.Opened.Patch;

        transcript = new ConsoleTranscript(options, output, error, () => session?.Run?.Turns ?? 0);
        session = new AssistantSession(transcript, logFolder);

        var saved = SavedConversation.Read(about.Conversation);

        // As the editor shows a conversation saved with the patch, before it knows
        // whether the next message carries it on.
        if (saved is not null) transcript.Seed(saved.Transcript);

        var because = options.Fresh ? string.Empty : saved?.Unresumable(limit, assistant, config.Values);

        Begin(saved is not null && because is null ? saved : null, because);
    }

    /// <summary>Whether the last turn ended in a failure.</summary>
    public bool Failed { get; private set; }

    /// <summary>
    /// A conversation over the patch as it stands, carrying on <paramref name="resuming"/>
    /// where there is one, and saying <paramref name="because"/> where one was set aside.
    /// </summary>
    private void Begin(SavedConversation? resuming, string? because)
    {
        var run = new AssistantRun(
            assistant,
            config,
            modules,
            current,
            limit,
            samples: about.Opened.Samples,
            pictures: about.Opened.Pictures,
            resuming: resuming,
            prose: settings.Prose(settingsPath),
            presets: presets);

        run.Workbench.Calling += transcript.Called;

        var senses = assistant.Senses(config.Values);

        transcript.Emit("started", new
        {
            provider = assistant.Id,
            conversation = resuming is null ? "new" : "resumed",
            turn = run.Turns,
            context = run.Tokens.Context,
            contextLimit = limit,
            file = about.Into.FullName,
            sees = senses.Vision,
            hears = senses.Hearing.ToString().ToLowerInvariant(),
        }, (resuming is null
            ? $"A new conversation with {assistant.Name} about {about.Name}."
            : $"Carrying on the conversation with {assistant.Name} about {about.Name}, after {Writing.Count(run.Turns, "turn")}.")
            + " " + Senses(senses));

        session.Begin(run, assistant, config, settings, resuming is not null, because);
    }

    /// <summary>What it can take in, said before anything is asked, with the setting that would let it hear.</summary>
    private string Senses(AssistantSenses senses)
    {
        var sees = senses.Vision ? "It sees the picture" : "It cannot see the picture";

        if (senses.Hearing != Listener.None) return $"{sees} and hears the sound.";

        var switchable = assistant.Form(config.Values).Any(field => field.Key == AssistantSchema.HearingKey);

        return switchable
            ? $"{sees}, and cannot hear the sound: --set {AssistantSchema.HearingKey}=true lets it."
            : $"{sees}, and cannot hear the sound.";
    }

    /// <summary>One turn: asked, shown, and written back with the conversation where it was answered.</summary>
    public async Task Ask(string message, CancellationToken cancel)
    {
        if (session.Spent(assistant, config) is { } spent) Begin(null, spent);

        var run = session.Run!;

        run.CatchUp(current);

        Failed = false;
        var answered = false;

        var clock = Stopwatch.StartNew();
        int requests = 0, input = 0, cached = 0, written = 0;
        var waited = TimeSpan.Zero;

        await foreach (var happened in session.Ask(message, cancel))
        {
            if (happened is PatchEvent.Failed) Failed = true;
            else answered = true;

            if (happened is PatchEvent.Cost cost)
            {
                requests++;
                input += cost.Input;
                cached += cost.CacheRead;
                written += cost.Output;
            }
            else if (TurnLoop.Waited(happened) is { } wait)
            {
                waited += wait;
            }
        }

        Spent(requests, input, cached, written, waited, clock.Elapsed);

        if (!answered) return;

        // A turn somebody stopped is not one to act on, as in the editor.
        var proposed = cancel.IsCancellationRequested ? null : run.Proposal;

        if (proposed is not null)
        {
            current = proposed;
            run.Rebase(current);
        }

        var problem = about.Write(current, proposed is not null, session.Save().ToJson(), modules, store);

        if (problem is not null)
        {
            error.WriteLine(AskedPatch.Complaint(problem));
            Failed = true;

            return;
        }

        transcript.Emit("wrote", new
        {
            file = about.Into.FullName,
            proposed = proposed is not null,
            turn = run.Turns,
            context = run.Tokens.Context,
            contextLimit = limit,
        }, (proposed is null ? $"Kept the conversation with {about.Into.Name}" : $"Wrote {about.Into.Name}")
            + string.Create(CultureInfo.InvariantCulture, $" — turn {run.Turns}, {run.Tokens.Context:N0} of {limit:N0} tokens of context."));
    }

    /// <summary>What the turn cost: the requests that reported their tokens, and the time spent, waiting included.</summary>
    private void Spent(int requests, int input, int cached, int output, TimeSpan waited, TimeSpan took)
    {
        var prose = string.Create(
            CultureInfo.InvariantCulture,
            $"{ConsoleTranscript.Aside}{Writing.Count(requests, "request")}: {input} tokens in ({cached} cached), {output} out, in {took.TotalSeconds:0}s");

        if (waited > TimeSpan.Zero)
            prose += string.Create(CultureInfo.InvariantCulture, $", {waited.TotalSeconds:0}s of it waiting to be let back in");

        transcript.Emit("turn", new
        {
            requests,
            input,
            cacheRead = cached,
            output,
            waited = Math.Round(waited.TotalSeconds, 1),
            seconds = Math.Round(took.TotalSeconds, 1),
        }, prose + ".");
    }

    public void Dispose() => session.Dispose();
}
