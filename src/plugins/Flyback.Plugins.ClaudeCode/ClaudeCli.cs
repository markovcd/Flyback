using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Programs;

namespace Flyback.Plugins.ClaudeCode;

/// <summary>
/// Claude Code run as a program, one process per question.
/// </summary>
/// <remarks>
/// Every built-in tool, setting, skill and server is off, so what answers is the
/// model and the signed-in plan and nothing of the person's setup. The key
/// variables are taken out of the environment it is given: a key there would be
/// billed instead of the plan, and no key is what this is for.
/// </remarks>
internal sealed partial class ClaudeCli(string executable) : IProgram
{
    private const string Name = "Claude Code";

    private static readonly string[] Keys = ["ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN"];

    /// <summary>Claude Code puts its session id in the prompt, so a fresh one per process would make every request miss the cache.</summary>
    internal const string SessionId = "f1a4b0c2-7e3d-4a58-9b16-2c5d8e0f4a71";

    /// <summary>A model name the command line may carry: an alias or an id, never anything that reads as a flag.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._\[\]-]{0,63}$")]
    private static partial Regex ModelName();

    /// <summary>Whether <paramref name="model"/> may be handed to the program.</summary>
    public static bool IsModel(string? model) => model is not null && ModelName().IsMatch(model);

    /// <summary>Effort as the program spells it. Sent at medium too, since the program's default can change.</summary>
    private static string Spelled(AssistantEffort effort) => effort switch
    {
        AssistantEffort.Low => "low",
        AssistantEffort.High => "high",
        _ => "medium",
    };

    /// <summary>The command line for <paramref name="request"/>.</summary>
    public static IReadOnlyList<string> Arguments(ClaudeRequest request)
    {
        if (!IsModel(request.Model))
            throw new ProgramFailure($"'{request.Model}' is not a model name Claude Code takes.");

        List<string> arguments =
        [
            "--print",
            "--input-format", "stream-json",
            "--output-format", "stream-json",
            "--verbose",
            "--tools", "",
            "--strict-mcp-config",
            "--disable-slash-commands",
            "--setting-sources", "",
            "--no-session-persistence",
            "--session-id", SessionId,
            "--system-prompt", ClaudeWire.System,
            "--model", request.Model,
            "--effort", Spelled(request.Effort),
        ];

        return arguments;
    }

    /// <summary>The one line of standard input for <paramref name="request"/>.</summary>
    public static string Input(ClaudeRequest request) => new JsonObject
    {
        ["type"] = "user",
        ["message"] = new JsonObject { ["role"] = "user", ["content"] = request.Content.DeepClone() },
    }.ToJsonString();

    /// <summary>The question as Claude Code is sent it.</summary>
    public static ClaudeRequest Request(ProgramQuestion question) =>
        new(question.Model, question.Effort, ClaudeWire.Content(question.Preamble, question.Turns));

    public Task<ProgramAnswer> Ask(ProgramQuestion question, CancellationToken cancel) => Ask(Request(question), cancel);

    public async Task<ProgramAnswer> Ask(ClaudeRequest request, CancellationToken cancel)
    {
        var (output, errors, exitCode) = await ProgramProcess.Run(
            Name,
            executable,
            Arguments(request),
            Input(request) + "\n",
            ProgramProcess.QuietFolder("flyback-claude-code"),
            Keys,
            cancel).ConfigureAwait(false);

        return Answer(output, errors, exitCode);
    }

    /// <summary>What the program wrote, as the answer, or as the reason there is none.</summary>
    public static ProgramAnswer Answer(string output, string errors, int exitCode)
    {
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Reverse())
        {
            JsonNode? parsed;

            try
            {
                parsed = JsonNode.Parse(line);
            }
            catch (JsonException)
            {
                continue;
            }

            if (parsed is not JsonObject { } result || (string?)result["type"] != "result") continue;

            var text = (string?)result["result"] ?? string.Empty;

            if (result["is_error"]?.GetValueKind() == JsonValueKind.True || exitCode != 0)
                throw new ProgramFailure(Explained(text, errors));

            var usage = result["usage"];

            return new ProgramAnswer(
                text,
                Tokens.Count(usage, "input_tokens") + Tokens.Count(usage, "cache_creation_input_tokens")
                    + Tokens.Count(usage, "cache_read_input_tokens"),
                Tokens.Count(usage, "cache_read_input_tokens"),
                Tokens.Count(usage, "output_tokens"));
        }

        throw new ProgramFailure(Explained(string.Empty, errors));
    }

    private static string Explained(string said, string errors) => ProgramReason.Explained(
        said,
        errors,
        Name,
        SignedOut(),
        "Claude Code is not signed in. Run `claude` in a terminal once and sign in with /login.");

    [GeneratedRegex(@"not logged in|/login|authenticat|credentials|unauthori[sz]ed|\b401\b", RegexOptions.IgnoreCase)]
    private static partial Regex SignedOut();
}
