using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Flyback.Plugins.Assist;

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
/// tell the model to ignore. The conversation goes in on standard input because it outgrows a
/// command line. The key variables are taken out of the environment it is given: a
/// key there would be billed instead of the plan, and no key is what this is for.
/// </remarks>
internal sealed partial class CodexCli(string executable) : ICodexCli
{
    /// <summary>The model setting that leaves the choice to Codex, which knows what the plan offers.</summary>
    public const string DefaultModel = "default";

    /// <summary>The longest one question may take, a backstop for a program that hangs.</summary>
    private static readonly TimeSpan Longest = TimeSpan.FromMinutes(15);

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
    private static partial Regex Name();

    /// <summary>Whether <paramref name="model"/> may be handed to the program.</summary>
    public static bool IsModel(string? model) => model is not null && (model == DefaultModel || Name().IsMatch(model));

    /// <summary>The command line for <paramref name="request"/>.</summary>
    /// <param name="request">The question.</param>
    /// <param name="instructions">The file holding what Codex runs on.</param>
    /// <param name="pictures">The files its pictures were written to, in order.</param>
    public static IReadOnlyList<string> Arguments(CodexRequest request, string instructions, IReadOnlyList<string> pictures)
    {
        if (!IsModel(request.Model))
            throw new CodexFailure($"'{request.Model}' is not a model name Codex takes.");

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

        if (request.Effort != AssistantEffort.Medium)
            arguments.AddRange(["-c", $"model_reasoning_effort=\"{(request.Effort == AssistantEffort.Low ? "low" : "high")}\""]);

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
        if (path.Any(char.IsControl)) throw new CodexFailure("A file name with a control character cannot be handed to Codex.");

        return "\"" + path.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    public async Task<CodexAnswer> Ask(CodexRequest request, CancellationToken cancel)
    {
        var folder = Quiet();
        var scratch = Directory.CreateDirectory(Path.Combine(folder, "ask-" + Guid.NewGuid().ToString("N"))).FullName;

        try
        {
            var instructions = Path.Combine(scratch, "instructions.md");

            await File.WriteAllTextAsync(instructions, Protocol.System, cancel).ConfigureAwait(false);

            List<string> pictures = [];

            foreach (var png in request.Pictures)
            {
                var file = Path.Combine(scratch, $"picture-{pictures.Count + 1}.png");

                await File.WriteAllBytesAsync(file, png, cancel).ConfigureAwait(false);
                pictures.Add(file);
            }

            return await Run(request, instructions, pictures, folder, cancel).ConfigureAwait(false);
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

    private async Task<CodexAnswer> Run(
        CodexRequest request, string instructions, IReadOnlyList<string> pictures, string folder, CancellationToken cancel)
    {
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = folder,
        };

        foreach (var argument in Arguments(request, instructions, pictures)) start.ArgumentList.Add(argument);
        foreach (var key in Keys) start.Environment.Remove(key);

        using var process = new Process { StartInfo = start };

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            throw new CodexFailure($"Codex would not start: {ex.Message}");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(Longest);

        await using var stopper = timeout.Token.Register(() => Stop(process));

        var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var errors = process.StandardError.ReadToEndAsync(CancellationToken.None);

        try
        {
            await process.StandardInput.WriteAsync(request.Prompt.AsMemory(), timeout.Token).ConfigureAwait(false);
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // It exited before reading; what it said is in the output.
        }

        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);

        cancel.ThrowIfCancellationRequested();

        if (timeout.IsCancellationRequested)
            throw new CodexFailure($"Codex took longer than {Longest.TotalMinutes:0} minutes and was stopped.");

        return Answer(await output.ConfigureAwait(false), await errors.ConfigureAwait(false), process.ExitCode);
    }

    /// <summary>What the program wrote, as the answer, or as the reason there is none.</summary>
    /// <remarks>
    /// Events are lines of JSON. The answer is the last message the model wrote; an
    /// item that is not a message is a tool the sandbox met and is not read, and a
    /// warning about a setting the program does not know is an item of its own.
    /// </remarks>
    public static CodexAnswer Answer(string output, string errors, int exitCode)
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

        if (failed is not null) throw new CodexFailure(Explained(failed, errors));

        if (text is null || exitCode != 0) throw new CodexFailure(Explained(string.Empty, errors));

        var cached = Count(usage, "cached_input_tokens");

        return new CodexAnswer(text, Math.Max(0, Count(usage, "input_tokens") - cached), cached, Count(usage, "output_tokens"));
    }

    private static int Count(JsonNode? usage, string name) =>
        usage?[name] is JsonValue value && value.TryGetValue<int>(out var count) ? count : 0;

    /// <summary>Why it failed, with the one thing a person can do about being signed out said outright.</summary>
    private static string Explained(string said, string errors)
    {
        var reason = !string.IsNullOrWhiteSpace(said) ? said.Trim() : errors.Trim();

        if (reason.Length > 600) reason = reason[..600] + "…";

        if (SignedOut().IsMatch(reason))
            return "Codex is not signed in. Run `codex login` in a terminal and sign in with ChatGPT. " + reason;

        return reason.Length > 0 ? $"Codex failed: {reason}" : "Codex exited without answering.";
    }

    [GeneratedRegex(@"not logged in|log ?in\b|authenticat|credentials|unauthori[sz]ed|\b401\b", RegexOptions.IgnoreCase)]
    private static partial Regex SignedOut();

    /// <summary>A folder with nothing in it, so no project's instructions are found by looking around.</summary>
    private static string Quiet()
    {
        var folder = Path.Combine(Path.GetTempPath(), "flyback-codex");

        Directory.CreateDirectory(folder);

        return folder;
    }

    private static void Stop(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Gone already.
        }
    }
}
