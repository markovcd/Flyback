using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Flyback.Plugins.Assist;

namespace Flyback.Plugins.ClaudeCode;

/// <summary>
/// Claude Code run as a program, one process per question.
/// </summary>
/// <remarks>
/// Every built-in tool, setting, skill and server is off, so what answers is the
/// model and the signed-in plan and nothing of the person's setup. The conversation
/// goes in on standard input because it outgrows a command line. The key
/// variables are taken out of the environment it is given: a key there would be
/// billed instead of the plan, and no key is what this is for.
/// </remarks>
internal sealed partial class ClaudeCli(string executable) : IClaudeCli
{
    /// <summary>The longest one question may take, a backstop for a program that hangs.</summary>
    private static readonly TimeSpan Longest = TimeSpan.FromMinutes(15);

    private static readonly string[] Keys = ["ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN"];

    /// <summary>A model name the command line may carry: an alias or an id, never anything that reads as a flag.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._\[\]-]{0,63}$")]
    private static partial Regex Name();

    /// <summary>Whether <paramref name="model"/> may be handed to the program.</summary>
    public static bool IsModel(string? model) => model is not null && Name().IsMatch(model);

    /// <summary>The command line for <paramref name="request"/>.</summary>
    public static IReadOnlyList<string> Arguments(ClaudeRequest request)
    {
        if (!IsModel(request.Model))
            throw new ClaudeCodeFailure($"'{request.Model}' is not a model name Claude Code takes.");

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
            "--system-prompt", Protocol.System,
            "--model", request.Model,
        ];

        if (request.Effort != AssistantEffort.Medium)
            arguments.AddRange(["--effort", request.Effort == AssistantEffort.Low ? "low" : "high"]);

        return arguments;
    }

    /// <summary>The one line of standard input for <paramref name="request"/>.</summary>
    public static string Input(ClaudeRequest request) => new JsonObject
    {
        ["type"] = "user",
        ["message"] = new JsonObject { ["role"] = "user", ["content"] = request.Content.DeepClone() },
    }.ToJsonString();

    public async Task<ClaudeAnswer> Ask(ClaudeRequest request, CancellationToken cancel)
    {
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Quiet(),
        };

        foreach (var argument in Arguments(request)) start.ArgumentList.Add(argument);
        foreach (var key in Keys) start.Environment.Remove(key);

        using var process = new Process { StartInfo = start };

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            throw new ClaudeCodeFailure($"Claude Code would not start: {ex.Message}");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(Longest);

        await using var stopper = timeout.Token.Register(() => Stop(process));

        var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var errors = process.StandardError.ReadToEndAsync(CancellationToken.None);

        try
        {
            await process.StandardInput.WriteLineAsync(Input(request).AsMemory(), timeout.Token).ConfigureAwait(false);
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // It exited before reading; what it said is in the output.
        }

        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);

        cancel.ThrowIfCancellationRequested();

        if (timeout.IsCancellationRequested)
            throw new ClaudeCodeFailure($"Claude Code took longer than {Longest.TotalMinutes:0} minutes and was stopped.");

        return Answer(await output.ConfigureAwait(false), await errors.ConfigureAwait(false), process.ExitCode);
    }

    /// <summary>What the program wrote, as the answer, or as the reason there is none.</summary>
    public static ClaudeAnswer Answer(string output, string errors, int exitCode)
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
                throw new ClaudeCodeFailure(Explained(text, errors));

            var usage = result["usage"];

            return new ClaudeAnswer(
                text,
                Count(usage, "input_tokens") + Count(usage, "cache_creation_input_tokens"),
                Count(usage, "cache_read_input_tokens"),
                Count(usage, "output_tokens"));
        }

        throw new ClaudeCodeFailure(Explained(string.Empty, errors));
    }

    private static int Count(JsonNode? usage, string name) =>
        usage?[name] is JsonValue value && value.TryGetValue<int>(out var count) ? count : 0;

    /// <summary>Why it failed, with the one thing a person can do about being signed out said outright.</summary>
    private static string Explained(string said, string errors)
    {
        var reason = !string.IsNullOrWhiteSpace(said) ? said.Trim() : errors.Trim();

        if (reason.Length > 600) reason = reason[..600] + "…";

        if (SignedOut().IsMatch(reason))
            return "Claude Code is not signed in. Run `claude` in a terminal once and sign in with /login. " + reason;

        return reason.Length > 0 ? $"Claude Code failed: {reason}" : "Claude Code exited without answering.";
    }

    [GeneratedRegex(@"not logged in|/login|authenticat|credentials|unauthori[sz]ed|\b401\b", RegexOptions.IgnoreCase)]
    private static partial Regex SignedOut();

    /// <summary>A folder with nothing in it, so no project's instructions are found by looking around.</summary>
    private static string Quiet()
    {
        var folder = Path.Combine(Path.GetTempPath(), "flyback-claude-code");

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
