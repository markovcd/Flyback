using System.Text.Json;
using System.Text.Json.Nodes;
using Flyback.Plugins.Assist;

namespace Flyback.Plugins.Codex;

/// <summary>
/// One conversation with Codex.
/// </summary>
/// <remarks>
/// Only the format is here; the turn is <see cref="TurnLoop"/>'s, run by the host
/// (ADR-0161). Each exchange is a fresh process sent the whole conversation, which
/// is what the prompt cache is for: the briefing and everything before the newest
/// turn are the same bytes as last time.
/// </remarks>
internal sealed class CodexSession : IModelConversation
{
    /// <summary>Said where a picture was, once it has gone from the conversation.</summary>
    internal const string PictureGone = "[A picture was shown here and is not kept. Render again to look again.]";

    private readonly PatchWorkbench workbench;
    private readonly AssistantChoices chosen;
    private readonly ICodexCli codex;
    private readonly List<Turn> turns = [];
    private readonly string preamble;

    public CodexSession(PatchWorkbench workbench, AssistantChoices chosen, ICodexCli codex)
    {
        this.workbench = workbench;
        this.chosen = chosen;
        this.codex = codex;

        preamble = Protocol.Preamble(workbench.Briefing, workbench.Tools);
    }

    PatchWorkbench IModelConversation.Workbench => workbench;

    bool IModelConversation.HearsItself => false;

    string? IModelConversation.EarModel => null;

    void IModelConversation.Add(string instruction) => turns.Add(new Turn(Turn.Person, instruction, []));

    void IModelConversation.Add(IReadOnlyList<ToolAnswer> answers) =>
        turns.Add(new Turn(
            Turn.Flyback,
            Protocol.Answers(answers),
            [.. answers.Select(answer => answer.Png).OfType<byte[]>()]));

    void IModelConversation.Forget()
    {
        for (var i = 0; i < turns.Count; i++)
        {
            if (turns[i].Pictures.Count == 0) continue;

            turns[i] = turns[i] with { Text = turns[i].Text + "\n" + PictureGone, Pictures = [] };
        }
    }

    async Task<ModelReply> IModelConversation.Send(CancellationToken cancel)
    {
        var (prompt, pictures) = Protocol.Conversation(preamble, turns);
        var request = new CodexRequest(chosen.Model, chosen.Effort, prompt, pictures);
        var answer = await codex.Ask(request, cancel).ConfigureAwait(false);

        turns.Add(new Turn(Turn.Model, answer.Text, []));

        var (text, calls) = Protocol.Parse(answer.Text);

        return new ModelReply(text, calls, answer.Input, answer.Cached, answer.Output);
    }

    /// <summary>Never asked: this provider offers no ear.</summary>
    Task<string?> IModelConversation.Listen(string model, string briefing, byte[] wav, CancellationToken cancel) =>
        Task.FromResult<string?>(null);

    /// <summary>The conversation without the briefing or any picture, as a list of who said what.</summary>
    public string Save()
    {
        var kept = new JsonArray();

        foreach (var turn in turns)
        {
            kept.Add(new JsonObject
            {
                ["role"] = turn.Role,
                ["text"] = turn.Pictures.Count == 0 ? turn.Text : turn.Text + "\n" + PictureGone,
            });
        }

        return kept.ToJsonString();
    }

    /// <summary>
    /// Takes up what <see cref="Save"/> wrote, before anything has been asked.
    /// False for anything else, which leaves this session as empty as it was.
    /// </summary>
    internal bool Take(string saved)
    {
        List<Turn> read = [];

        try
        {
            if (JsonNode.Parse(saved) is not JsonArray array) return false;

            foreach (var item in array)
            {
                if (item is not JsonObject entry
                    || entry["role"] is not JsonValue role || !role.TryGetValue<string>(out var who) || !Turn.IsRole(who)
                    || entry["text"] is not JsonValue text || !text.TryGetValue<string>(out var said))
                    return false;

                read.Add(new Turn(who, said, []));
            }
        }
        catch (JsonException)
        {
            return false;
        }

        turns.AddRange(read);

        return true;
    }

    public void Dispose()
    {
    }
}
