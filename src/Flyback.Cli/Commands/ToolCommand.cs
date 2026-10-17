using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Flyback.Cli.Common;
using Flyback.Cli.Models;
using Flyback.Core;
using Flyback.Core.Graph;
using Flyback.Engine.Graph;
using Flyback.Engine.Language;
using Flyback.Engine.Render;
using Flyback.Plugins.Assist;
using Flyback.Plugins.Hosting;
using PluginRegistry = Flyback.Cli.Plugins;

namespace Flyback.Cli.Commands;

/// <summary>
/// The assistant's tools as commands: one call on a patch file, run on the workbench
/// <c>ask</c> builds, with each edit kept for undo (ADR-0202).
/// </summary>
/// <remarks>
/// For a coding assistant the person runs themselves. It is the model, so nothing here
/// talks to one: the client reads <c>--briefing</c>, calls tools by name and reads back
/// what they said.
/// </remarks>
internal static class ToolCommand
{
    private static readonly JsonSerializerOptions Line = new(Writing.Json) { WriteIndented = false };

    public static Command Build(PluginRegistry plugins, Option<bool> json)
    {
        var name = new Argument<string?>("name")
        {
            Description = "The tool to call, as --list names it.",
            Arity = ArgumentArity.ZeroOrOne,
        };

        var patch = PatchArgument();

        var list = new Option<bool>("--list") { Description = "List every tool with the schema of its arguments." };

        var briefing = new Option<bool>("--briefing")
        {
            Description = "Print the briefing a model is handed: the conventions, the modules and the presets.",
        };

        var skill = new Option<bool>("--skill")
        {
            Description = "Print a SKILL.md that teaches Claude Code or Codex to build patches with these commands.",
        };

        var arguments = new Option<string>("--args")
        {
            Description = "The call's arguments, as a JSON object.",
            DefaultValueFactory = _ => "{}",
        };

        var picture = new Option<FileInfo?>("--picture")
        {
            Description = "Where a picture the call draws is written. Left out, it goes to the temporary folder.",
        };

        var sound = new Option<FileInfo?>("--sound")
        {
            Description = "Where a sound the call plays is written. Left out, it goes to the temporary folder.",
        };

        var command = new Command(
            "tool",
            "Call one of the assistant's tools on a patch file and write the patch back, the way the editor's "
            + "assistant does. For driving Flyback from a coding assistant you run yourself.")
        {
            name, patch, list, briefing, skill, arguments, picture, sound, json,
            Step("undo", "Take back the last edit a tool made to the patch.", plugins, json, Undo, to: true),
            Step("redo", "Make again the last edit undo took back.", plugins, json, Redo, to: false),
            Step("history", "List the edits tools have made to the patch, oldest first.", plugins, json, History, to: false),
        };

        command.SetAction(async (result, cancel) =>
        {
            var output = result.InvocationConfiguration.Output;
            var error = result.InvocationConfiguration.Error;
            var asJson = result.GetValue(json);

            if (result.GetValue(skill))
            {
                output.Write(Skill);
                return Exit.Ok;
            }

            plugins.Ready();

            if (result.GetValue(list)) return List(plugins.Catalog, asJson, output);
            if (result.GetValue(briefing)) return Briefing(plugins.Catalog, output);

            if (result.GetValue(name) is not { } tool || result.GetValue(patch) is not { } file)
            {
                error.WriteLine(AskedPatch.Complaint("say which tool to call and on what patch: tool <name> <patch> --args '{...}'."));
                return Exit.Failed;
            }

            return await Call(
                plugins.Catalog,
                new ToolHistory(),
                file,
                tool,
                result.GetValue(arguments)!,
                result.GetValue(picture),
                result.GetValue(sound),
                asJson,
                output,
                error,
                cancel).ConfigureAwait(false);
        });

        return command;
    }

    private static Argument<FileInfo?> PatchArgument() => new("patch")
    {
        Description = $"The patch file: {AskedPatch.Formats}. One that does not exist yet starts empty.",
        Arity = ArgumentArity.ZeroOrOne,
    };

    private delegate int Stepper(ToolHistory history, FileInfo file, int? to, bool json, TextWriter output, TextWriter error);

    private static Command Step(string verb, string description, PluginRegistry plugins, Option<bool> json, Stepper run, bool to)
    {
        var patch = PatchArgument();
        patch.Arity = ArgumentArity.ExactlyOne;

        var back = new Option<int?>("--to") { Description = "Go back to after this many edits, 0 for none, as history numbers them." };

        var command = new Command(verb, description) { patch, json };

        if (to) command.Add(back);

        command.SetAction(result => run(
            new ToolHistory(),
            result.GetValue(patch)!,
            to ? result.GetValue(back) : null,
            result.GetValue(json),
            result.InvocationConfiguration.Output,
            result.InvocationConfiguration.Error));

        return command;
    }

    /// <summary>The SKILL.md that ships with the command, in LF line endings however the repo was checked out.</summary>
    internal static string Skill
    {
        get
        {
            using var stream = typeof(ToolCommand).Assembly.GetManifestResourceStream("Flyback.Cli.SKILL.md")!;
            using var reader = new StreamReader(stream);

            return reader.ReadToEnd().ReplaceLineEndings("\n");
        }
    }

    /// <summary>The workbench a call runs on, built over <paramref name="over"/>.</summary>
    private static PatchWorkbench Bench(PluginCatalog plugins, Patch over, string? settingsPath, AskedPatch? about = null) => new(
        plugins.Modules,
        over,
        vision: true,
        hearing: Listener.None,
        samples: about?.Opened.Samples,
        pictures: about?.Opened.Pictures,
        prose: AssistantSettings.Load(settingsPath).Prose(settingsPath ?? SettingsFile.Path),
        presets: plugins.Presets);

    public static int List(PluginCatalog plugins, bool json, TextWriter output, string? settingsPath = null)
    {
        var tools = Bench(plugins, Presets.Empty(plugins.Modules), settingsPath).Tools;

        if (json)
        {
            output.WriteLine(new JsonArray([.. tools.Select(tool => (JsonNode)new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["inputSchema"] = Schema(tool),
            })]).ToJsonString(Writing.Json));

            return Exit.Ok;
        }

        foreach (var tool in tools)
        {
            output.WriteLine(tool.Name);
            output.WriteLine($"  {tool.Description}");
            output.WriteLine($"  --args {Schema(tool)["properties"]?.ToJsonString(Line) ?? "{}"}");
        }

        return Exit.Ok;
    }

    /// <summary>A tool's arguments as a whole JSON Schema object, which is what an MCP client is handed.</summary>
    internal static JsonObject Schema(PatchTool tool)
    {
        var schema = JsonNode.Parse(tool.Schema) as JsonObject ?? [];
        var whole = new JsonObject { ["type"] = "object" };

        foreach (var (key, value) in schema) whole[key] = value?.DeepClone();

        return whole;
    }

    public static int Briefing(PluginCatalog plugins, TextWriter output, string? settingsPath = null)
    {
        output.WriteLine(Bench(plugins, Presets.Empty(plugins.Modules), settingsPath).Briefing);
        return Exit.Ok;
    }

    /// <summary>
    /// Runs <paramref name="tool"/> on <paramref name="file"/>, writes the patch back where
    /// the call changed it, and keeps the edit in <paramref name="history"/>.
    /// </summary>
    public static async Task<int> Call(
        PluginCatalog plugins,
        ToolHistory history,
        FileInfo file,
        string tool,
        string arguments,
        FileInfo? picture,
        FileInfo? sound,
        bool json,
        TextWriter output,
        TextWriter error,
        CancellationToken cancel,
        string? settingsPath = null)
    {
        JsonElement given;

        try
        {
            given = JsonDocument.Parse(arguments).RootElement.Clone();
        }
        catch (JsonException ex)
        {
            error.WriteLine(AskedPatch.Complaint($"--args is not JSON: {ex.Message}"));
            return Exit.Failed;
        }

        if (given.ValueKind != JsonValueKind.Object)
        {
            error.WriteLine(AskedPatch.Complaint("--args is a JSON object, such as '{\"type_id\": \"sine\"}'."));
            return Exit.Failed;
        }

        if (AskCommand.Open(plugins, file, null, null, error) is not { } about) return Exit.Failed;

        var modules = plugins.Modules;
        var current = file.Exists ? File.ReadAllBytes(file.FullName) : null;
        var kept = history.Find(file.FullName);
        var log = kept is not null && kept.Matches(current)
            ? kept
            : new ToolLog(file.FullName, PatchIO.ToJson(about.Opened.Patch, modules), current, [], 0);

        var bench = Bench(plugins, PatchIO.Read(log.Start, modules).Patch, settingsPath, about);

        if (log.At > 0) bench.Restore(new WorkbenchState(log.Start, log.Working(log.At), log.Handles(log.At)!, 0, 0));

        var outcome = await bench.InvokeAsync(tool, given, cancel).ConfigureAwait(false);

        var pictureAt = outcome.Png is { } png ? Media(png, picture, file, "png", error) : null;
        var soundAt = outcome.Wav is { } wav ? Media(wav, sound, file, "wav", error) : null;

        var wrote = false;

        if (outcome.Ok && bench.Edits > 0)
        {
            var patch = bench.Snapshot();
            var state = bench.Save();
            byte[] bytes;

            try
            {
                bytes = Bytes(patch, about, modules);
                File.WriteAllBytes(file.FullName, bytes);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                error.WriteLine(AskedPatch.Complaint($"{file.Name}: {ex.Message}"));
                return Exit.Failed;
            }

            if (kept is not null && !ReferenceEquals(kept, log))
                error.WriteLine($"{file.Name} changed since the last tool call, so its undo history starts again here.");

            log = log.Then(new ToolStep(
                tool,
                JsonSerializer.Serialize(given, Line),
                bytes,
                PatchIO.ToJson(patch, modules),
                new Dictionary<string, Guid>(state.Handles, StringComparer.OrdinalIgnoreCase)));

            if (history.Keep(log) is { } problem) error.WriteLine(AskedPatch.Complaint(problem));

            wrote = true;
        }

        if (json)
        {
            output.WriteLine(new JsonObject
            {
                ["tool"] = tool,
                ["ok"] = outcome.Ok,
                ["text"] = outcome.Text,
                ["wrote"] = wrote,
                ["step"] = log.At,
                ["picture"] = pictureAt,
                ["sound"] = soundAt,
            }.ToJsonString(Line));
        }
        else
        {
            (outcome.Ok ? output : error).WriteLine(outcome.Ok ? outcome.Text : AskedPatch.Complaint($"{tool} refused: {outcome.Text}"));

            if (pictureAt is not null) output.WriteLine($"The picture is in {pictureAt}.");
            if (soundAt is not null) output.WriteLine($"The sound is in {soundAt}.");
            if (wrote) output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Wrote {file.Name}: edit {log.At}, which tool undo takes back."));
        }

        return outcome.Ok ? Exit.Ok : Exit.Problems;
    }

    /// <summary>The patch as the file's extension says to write it.</summary>
    private static byte[] Bytes(Patch patch, AskedPatch about, ModuleCatalog modules)
    {
        if (PatchFile.Bundled(about.Into))
        {
            using var packed = new MemoryStream();

            PatchBundle.Write(packed, patch, about.Carried, modules, about.Conversation);

            return packed.ToArray();
        }

        return System.Text.Encoding.UTF8.GetBytes(
            PatchFile.Sourced(about.Into) ? PatchPrinter.Print(patch, modules) : PatchIO.ToJson(patch, modules));
    }

    /// <summary>Writes a picture or a sound where it was asked for, or to the temporary folder, and says where.</summary>
    private static string? Media(byte[] bytes, FileInfo? into, FileInfo patch, string extension, TextWriter error)
    {
        var path = into?.FullName
            ?? Path.Combine(Path.GetTempPath(), "flyback-tool", $"{Path.GetFileNameWithoutExtension(patch.Name)}.{extension}");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);

            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error.WriteLine(AskedPatch.Complaint($"{Path.GetFileName(path)}: {ex.Message}"));
            return null;
        }
    }

    /// <summary>The history for <paramref name="file"/> while the file is as it left it, or null having said why not.</summary>
    private static ToolLog? Standing(ToolHistory history, FileInfo file, TextWriter error)
    {
        var log = history.Find(file.FullName);

        if (log is null || log.Steps.Count == 0)
        {
            error.WriteLine(AskedPatch.Complaint($"no tool has edited {file.Name}."));
            return null;
        }

        if (!log.Matches(file.Exists ? File.ReadAllBytes(file.FullName) : null))
        {
            error.WriteLine(AskedPatch.Complaint($"{file.Name} changed since the last tool call, so there is no history of it to step through."));
            return null;
        }

        return log;
    }

    public static int Undo(ToolHistory history, FileInfo file, int? to, bool json, TextWriter output, TextWriter error)
    {
        if (Standing(history, file, error) is not { } log) return Exit.Failed;

        var target = to ?? log.At - 1;

        if (target < 0 || target > log.At)
        {
            error.WriteLine(AskedPatch.Complaint(log.At == 0
                ? $"every edit to {file.Name} is undone already."
                : $"--to is 0 to {log.At}, how many edits to {file.Name} to keep."));

            return Exit.Failed;
        }

        return Move(history, file, log, target, json, output, error);
    }

    public static int Redo(ToolHistory history, FileInfo file, int? to, bool json, TextWriter output, TextWriter error)
    {
        if (Standing(history, file, error) is not { } log) return Exit.Failed;

        if (log.At == log.Steps.Count)
        {
            error.WriteLine(AskedPatch.Complaint($"nothing done to {file.Name} has been undone."));
            return Exit.Failed;
        }

        return Move(history, file, log, log.At + 1, json, output, error);
    }

    private static int Move(ToolHistory history, FileInfo file, ToolLog log, int target, bool json, TextWriter output, TextWriter error)
    {
        try
        {
            if (log.File(target) is { } bytes) File.WriteAllBytes(file.FullName, bytes);
            else File.Delete(file.FullName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error.WriteLine(AskedPatch.Complaint($"{file.Name}: {ex.Message}"));
            return Exit.Failed;
        }

        var moved = log with { At = target };

        if (history.Keep(moved) is { } problem) error.WriteLine(AskedPatch.Complaint(problem));

        if (json)
        {
            output.WriteLine(new JsonObject { ["step"] = target, ["steps"] = log.Steps.Count }.ToJsonString(Line));
        }
        else
        {
            output.WriteLine(target == 0
                ? $"{file.Name} is back as it was before any tool edited it."
                : string.Create(CultureInfo.InvariantCulture, $"{file.Name} is back after edit {target}, {log.Steps[target - 1].Tool}."));
        }

        return Exit.Ok;
    }

    public static int History(ToolHistory history, FileInfo file, int? to, bool json, TextWriter output, TextWriter error)
    {
        if (Standing(history, file, error) is not { } log) return Exit.Failed;

        if (json)
        {
            output.WriteLine(new JsonObject
            {
                ["step"] = log.At,
                ["steps"] = new JsonArray([.. log.Steps.Select((step, i) => (JsonNode)new JsonObject
                {
                    ["step"] = i + 1,
                    ["tool"] = step.Tool,
                    ["arguments"] = JsonNode.Parse(step.Arguments),
                    ["undone"] = i >= log.At,
                })]),
            }.ToJsonString(Writing.Json));

            return Exit.Ok;
        }

        for (var i = 0; i < log.Steps.Count; i++)
        {
            var step = log.Steps[i];

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{i + 1,3} {step.Tool} {step.Arguments}{(i >= log.At ? "  (undone)" : "")}"));
        }

        return Exit.Ok;
    }
}
