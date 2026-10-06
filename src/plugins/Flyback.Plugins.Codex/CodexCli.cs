using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Programs;

namespace Flyback.Plugins.Codex;

/// <summary>
/// Codex run as a program, one process per question.
/// </summary>
/// <remarks>
/// Its instructions are replaced, its user configuration and rules are not read,
/// and every tool, skill, plugin and search that can be switched off is, so what
/// answers is the model and the signed-in plan and nothing of the person's setup.
/// What stays is the sandbox, read-only in an empty folder, around two tools that
/// have no switch, and the person's global <c>AGENTS.md</c>, which the instructions
/// tell the model to ignore. The key variables are taken out of the environment it
/// is given: a key there would be billed instead of the plan, and no key is what
/// this is for.
/// </remarks>
internal sealed partial class CodexCli(string executable) : IProgram
{
    /// <summary>The model setting that leaves the choice to Codex, which knows what the plan offers.</summary>
    public const string DefaultModel = "default";

    private const string Name = "Codex";

    private static readonly string[] Keys = ["CODEX_API_KEY", "OPENAI_API_KEY"];

    /// <summary>Configuration that takes the program's surroundings out of the conversation.</summary>
    private static readonly string[] Settings =
    [
        "web_search=\"disabled\"",
        "include_permissions_instructions=false",
        "include_apps_instructions=false",
        "include_collaboration_mode_instructions=false",
        "include_environment_context=false",
        "skills.include_instructions=false",
        "skills.bundled.enabled=false",
        "project_doc_max_bytes=0",
    ];

    /// <summary>
    /// Features that give the model a tool or something to read. One the installed
    /// program does not know is ignored, so this list can outlive a version.
    /// </summary>
    private static readonly string[] Features =
    [
        "shell_tool", "unified_exec", "view_image", "goals", "apps", "plugins", "multi_agent",
        "skill_search", "tool_suggest", "image_generation", "computer_use", "browser_use", "hooks",
    ];

    /// <summary>A model name the command line may carry: an id, never anything that reads as a flag.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$")]
    private static partial Regex ModelName();

    /// <summary>Whether <paramref name="model"/> may be handed to the program.</summary>
    public static bool IsModel(string? model) => model is not null && (model == DefaultModel || ModelName().IsMatch(model));

    /// <summary>Effort as the program spells it. Sent at medium too, since the program's default can change.</summary>
    private static string Spelled(AssistantEffort effort) => effort switch
    {
        AssistantEffort.Low => "low",
        AssistantEffort.High => "high",
        _ => "medium",
    };

    /// <summary>The command line for <paramref name="request"/>.</summary>
    /// <param name="request">The question.</param>
    /// <param name="instructions">The file holding what Codex runs on.</param>
    /// <param name="pictures">The files its pictures were written to, in order.</param>
    public static IReadOnlyList<string> Arguments(CodexRequest request, string instructions, IReadOnlyList<string> pictures)
    {
        if (!IsModel(request.Model))
            throw new ProgramFailure($"'{request.Model}' is not a model name Codex takes.");

        // The prompt is standard input, named first so the pictures that end the line cannot take it.
        List<string> arguments =
        [
            "exec", "-",
            "--json",
            "--ephemeral",
            "--skip-git-repo-check",
            "--ignore-user-config",
            "--ignore-rules",
            "--sandbox", "read-only",
            "-c", $"model_instructions_file={Toml(instructions)}",
        ];

        foreach (var setting in Settings) arguments.AddRange(["-c", setting]);
        foreach (var feature in Features) arguments.AddRange(["-c", $"features.{feature}=false"]);

        if (request.Model != DefaultModel) arguments.AddRange(["--model", request.Model]);

        arguments.AddRange(["-c", $"model_reasoning_effort=\"{Spelled(request.Effort)}\""]);

        if (pictures.Count > 0)
        {
            arguments.Add("--image");
            arguments.AddRange(pictures);
        }

        return arguments;
    }

    /// <summary>A path as a TOML string, which a configuration override is read as.</summary>
    public static string Toml(string path)
    {
        if (path.Any(char.IsControl)) throw new ProgramFailure("A file name with a control character cannot be handed to Codex.");

        return "\"" + path.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    /// <summary>The question as Codex is sent it.</summary>
    public static CodexRequest Request(ProgramQuestion question)
    {
        var (prompt, pictures) = CodexWire.Conversation(question.Preamble, question.Turns);

        return new CodexRequest(question.Model, question.Effort, prompt, pictures);
    }

    public Task<ProgramAnswer> Ask(ProgramQuestion question, CancellationToken cancel) => Ask(Request(question), cancel);

    /// <summary>
    /// <paramref name="answer"/> naming the model it was asked of, which Codex's events do not.
    /// Null where the choice was left to Codex.
    /// </summary>
    public static ProgramAnswer Named(ProgramAnswer answer, CodexRequest request) =>
        answer with { Model = request.Model == DefaultModel ? null : request.Model };

    public async Task<ProgramAnswer> Ask(CodexRequest request, CancellationToken cancel)
    {
        var folder = ProgramProcess.QuietFolder("flyback-codex");
        var scratch = Directory.CreateDirectory(Path.Combine(folder, "ask-" + Guid.NewGuid().ToString("N"))).FullName;

        try
        {
            var instructions = Path.Combine(scratch, "instructions.md");

            await File.WriteAllTextAsync(instructions, CodexWire.System, cancel).ConfigureAwait(false);

            List<string> pictures = [];

            foreach (var png in request.Pictures)
            {
                var file = Path.Combine(scratch, $"picture-{pictures.Count + 1}.png");

                await File.WriteAllBytesAsync(file, png, cancel).ConfigureAwait(false);
                pictures.Add(file);
            }

            var (output, errors, exitCode) = await ProgramProcess.Run(
                Name, executable, Arguments(request, instructions, pictures), request.Prompt, folder, Keys, cancel)
                .ConfigureAwait(false);

            return Named(Answer(output, errors, exitCode), request);
        }
        finally
        {
            try
            {
                Directory.Delete(scratch, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Left for the system's own tidying of its temporary folder.
            }
        }
    }

    /// <summary>What the program wrote, as the answer, or as the reason there is none.</summary>
    /// <remarks>
    /// Events are lines of JSON. The answer is the last message the model wrote; an
    /// item that is not a message is a tool the sandbox met and is not read, and a
    /// warning about a setting the program does not know is an item of its own.
    /// </remarks>
    public static ProgramAnswer Answer(string output, string errors, int exitCode)
    {
        string? text = null;
        string? failed = null;
        JsonNode? usage = null;

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
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

            if (parsed is not JsonObject { } entry) continue;

            switch ((string?)entry["type"])
            {
                case "item.completed" when entry["item"] is JsonObject { } item && (string?)item["type"] == "agent_message":
                    text = (string?)item["text"];
                    break;

                case "turn.completed":
                    usage = entry["usage"];
                    break;

                case "turn.failed":
                    failed = (string?)entry["error"]?["message"] ?? string.Empty;
                    break;

                case "error":
                    failed ??= (string?)entry["message"] ?? string.Empty;
                    break;
            }
        }

        if (failed is not null) throw new ProgramFailure(Explained(failed, errors));

        if (text is null || exitCode != 0) throw new ProgramFailure(Explained(string.Empty, errors));

        return new ProgramAnswer(
            text,
            Tokens.Count(usage, "input_tokens"),
            Tokens.Count(usage, "cached_input_tokens"),
            Tokens.Count(usage, "output_tokens"));
    }

    private static string Explained(string said, string errors) => ProgramReason.Explained(
        said,
        errors,
        Name,
        SignedOut(),
        "Codex is not signed in. Run `codex login` in a terminal and sign in with ChatGPT.");

    [GeneratedRegex(@"not logged in|log ?in\b|authenticat|credentials|unauthori[sz]ed|\b401\b", RegexOptions.IgnoreCase)]
    private static partial Regex SignedOut();
}
