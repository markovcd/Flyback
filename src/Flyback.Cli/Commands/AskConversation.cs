using System.Text.Json;
using Flyback.Cli.Common;
using Flyback.Cli.Models;
using Flyback.Core.Graph;
using Flyback.Plugins.Assist;

namespace Flyback.Cli.Commands;

/// <summary>
/// One conversation driven from the console: the editor's <see cref="AssistantRun"/>
/// with a writer where the panel has a transcript, and a file where it has a canvas.
/// </summary>
internal sealed class AskConversation : IDisposable
{
    private static readonly JsonSerializerOptions Line = new(Writing.Json) { WriteIndented = false };

    private readonly IPatchAssistant assistant;
    private readonly AssistantConfig config;
    private readonly AssistantSettings settings;
    private readonly string? settingsPath;
    private readonly ModuleCatalog modules;
    private readonly IReadOnlyList<PatchPreset> presets;
    private readonly AskedPatch about;
    private readonly AskOptions options;
    private readonly ConversationStore store;
    private readonly string? logFolder;
    private readonly TextWriter output;
    private readonly TextWriter error;
    private readonly int limit;

    private readonly List<TranscriptLine> transcript = [];

    private AssistantRun run;
    private ConversationLog log;
    private Patch current;

    /// <summary>Pictures and sounds written to <see cref="AskOptions.Seen"/> so far.</summary>
    private int kept;

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
        this.options = options;
        this.store = store;
        this.logFolder = logFolder;
        this.output = output;
        this.error = error;

        limit = options.Turns ?? settings.TurnLimit;
        current = about.Opened.Patch;

        var saved = options.Fresh ? null : SavedConversation.Read(about.Conversation);
        var because = saved?.Unresumable(limit, assistant, config.Values);

        (run, log) = Begin(because is null ? saved : null, because);
    }

    /// <summary>Whether the last turn ended in a failure.</summary>
    public bool Failed { get; private set; }

    /// <summary>
    /// A conversation over the patch as it stands, carrying on <paramref name="resuming"/>
    /// where there is one, and saying <paramref name="because"/> where one was set aside.
    /// </summary>
    private (AssistantRun, ConversationLog) Begin(SavedConversation? resuming, string? because)
    {
        var started = new AssistantRun(
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

        started.Workbench.Calling += Called;

        var logging = ConversationLog.Start(settings.LogConversations, assistant.Id, logFolder);

        transcript.Clear();

        var senses = assistant.Senses(config.Values);

        Emit("started", new
        {
            provider = assistant.Id,
            conversation = resuming is null ? "new" : "resumed",
            turn = started.Turns,
            turnLimit = limit,
            file = about.Into.FullName,
            sees = senses.Vision,
            hears = senses.Hearing.ToString().ToLowerInvariant(),
        }, (resuming is null
            ? $"A new conversation with {assistant.Name} about {about.Name}."
            : $"Carrying on the conversation with {assistant.Name} about {about.Name}, after {Writing.Count(started.Turns, "turn")}.")
            + " " + Senses(senses));

        if (resuming is not null)
        {
            transcript.AddRange(resuming.Transcript);

            if (!started.PickedUp)
            {
                Note($"{assistant.Name} could not pick up what was said before, so it starts again from the patch it had built.");
            }

            return (started, logging);
        }

        if (because is { Length: > 0 }) Note(because);

        var briefing = $"The briefing it was handed:{Environment.NewLine}{started.Workbench.Briefing}";

        if (settings.ShowBriefing) logging.Write("briefing", briefing);
        if (options.Briefing) Emit("briefing", new { text = started.Workbench.Briefing }, briefing);

        return (started, logging);
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
        if (run.Exhausted)
        {
            run.Dispose();
            log.Dispose();

            (run, log) = Begin(null, "That conversation had its turns. Starting another.");
        }

        run.CatchUp(current);

        transcript.Add(new TranscriptLine(Voice.You, message));
        log.Write("you", config.Transport is KeyedTransport keyed ? keyed.Scrubbed(message)! : message);
        Emit("you", new { text = message }, null);

        if (run.Unsaid is { } told)
        {
            transcript.Add(new TranscriptLine(Voice.Aside, $"Told it what changed on the canvas: {told}."));
            log.Write("told", told);
            Emit("told", new { text = told }, Hung(Aside, $"told it what changed: {told}"));
        }

        Failed = false;
        var answered = false;

        await foreach (var happened in run.Ask(message, cancel))
        {
            var (line, kind, text) = TranscriptLine.Of(happened);

            transcript.Add(line);
            log.Write(kind, text);

            if (happened is not PatchEvent.Failed) answered = true;
            else Failed = true;

            Show(happened, kind, text);
        }

        if (!answered) return;

        // A turn somebody stopped is not one to act on, as in the editor.
        var proposed = cancel.IsCancellationRequested ? null : run.Proposal;

        if (proposed is not null)
        {
            current = proposed;
            run.Rebase(current);
        }

        var conversation = run.Save(transcript).ToJson();
        var problem = about.Write(current, proposed is not null, conversation, modules, store);

        if (problem is not null)
        {
            error.WriteLine(AskedPatch.Complaint(problem));
            Failed = true;

            return;
        }

        Emit("wrote", new
        {
            file = about.Into.FullName,
            proposed = proposed is not null,
            turn = run.Turns,
            turnLimit = limit,
        }, proposed is null
            ? $"Kept the conversation with {about.Into.Name} — turn {run.Turns} of {limit}."
            : $"Wrote {about.Into.Name} — turn {run.Turns} of {limit}.");
    }

    private void Show(PatchEvent happened, string kind, string text)
    {
        switch (happened)
        {
            case PatchEvent.Said:
                Emit(kind, new { text }, Hung(string.Empty, text));
                break;

            case PatchEvent.Read:
                Emit(kind, new { text }, Hung(Aside, $"looked up {First(text)}"));
                break;

            case PatchEvent.Saw saw:
                Seen(kind, text, saw.Png, "png");
                break;

            case PatchEvent.Heard heard:
                Seen(kind, text, heard.Wav, "wav");
                break;

            case PatchEvent.Cost cost:
                Emit(kind, new { text, input = cost.Input, cacheRead = cost.CacheRead, output = cost.Output }, Hung(Aside, text));
                break;

            case PatchEvent.Proposed:
                Emit(kind, new { text }, Hung("Proposed: ", text));
                break;

            case PatchEvent.Failed:
                Emit(kind, new { text }, Hung("! ", text));
                break;

            default:
                Emit(kind, new { text }, Hung(Aside, text));
                break;
        }
    }

    /// <summary>A tool call as it arrives, which the transcript leaves to its result.</summary>
    private void Called(string tool, JsonElement arguments)
    {
        var given = arguments.ValueKind == JsonValueKind.Undefined ? (JsonElement?)null : arguments.Clone();
        var shown = given is { } json ? Flattened(JsonSerializer.Serialize(json, Line)) : "{}";

        Emit("call", new { tool, arguments = given }, Hung("  → ", $"{tool} {Shortened(shown, 160)}"));
    }

    /// <summary>A picture or a sound it took in, written out where <see cref="AskOptions.Seen"/> says.</summary>
    private void Seen(string kind, string caption, byte[] bytes, string extension)
    {
        string? file = null;

        if (options.Seen is { } folder)
        {
            try
            {
                folder.Create();
                file = Path.Combine(folder.FullName, $"{run.Turns:00}-{++kept:00}-{kind}.{extension}");
                File.WriteAllBytes(file, bytes);
            }
            catch (Exception ex)
            {
                error.WriteLine(AskedPatch.Complaint($"{folder.Name}: {ex.Message}"));
                file = null;
            }
        }

        Emit(kind, new { text = caption, file }, Hung(Aside, file is null ? caption : $"{caption}{Environment.NewLine}→ {file}"));
    }

    private void Note(string text)
    {
        transcript.Add(new TranscriptLine(Voice.Note, text));
        Emit("note", new { text }, text);
    }

    /// <summary>One thing that happened: a JSON line under <c>--json</c>, else <paramref name="prose"/> where there is any.</summary>
    private void Emit(string kind, object fields, string? prose)
    {
        if (options.Json)
        {
            var body = JsonSerializer.SerializeToNode(fields, Line)!.AsObject();

            body.Insert(0, "kind", kind);
            output.WriteLine(body.ToJsonString(Line));
        }
        else if (prose is not null)
        {
            output.WriteLine(prose);
        }

        output.Flush();
    }

    /// <summary>How a step of the turn is marked, apart from what the assistant said.</summary>
    private const string Aside = "  · ";

    /// <summary>
    /// <paramref name="text"/> after <paramref name="marker"/>, its later lines hung
    /// under its first and blank lines at either end dropped.
    /// </summary>
    private static string Hung(string marker, string text)
    {
        var lines = text.ReplaceLineEndings("\n").Split('\n')
            .SkipWhile(string.IsNullOrWhiteSpace)
            .Reverse()
            .SkipWhile(string.IsNullOrWhiteSpace)
            .Reverse()
            .ToList();

        return lines.Count == 0
            ? marker.TrimEnd()
            : marker + string.Join(Environment.NewLine + new string(' ', marker.Length), lines);
    }

    /// <summary>The first line of <paramref name="text"/>, cut to a hundred characters.</summary>
    private static string First(string text) =>
        Shortened(text.AsSpan().Trim().ToString().ReplaceLineEndings("\n").Split('\n')[0], 100);

    /// <summary>Every line of <paramref name="text"/>, trimmed and joined onto one.</summary>
    private static string Flattened(string text) => string.Join(
        ' ',
        text.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim()));

    private static string Shortened(string text, int most) => text.Length > most ? $"{text[..most]}…" : text;

    public void Dispose()
    {
        run.Dispose();
        log.Dispose();
    }
}

